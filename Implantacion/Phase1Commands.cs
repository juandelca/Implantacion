using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors;
using System.Linq;

namespace Civil3D_Phase1
{
    public class TrackerModel
    {
        // ... (Esta clase no cambia) ...
        public string id_tracker { get; set; }
        public string fabricante { get; set; }
        public string modelo { get; set; }
        public string configuracion { get; set; }
        public double ancho_huella_ns { get; set; }
        public double longitud_largo { get; set; }
        public double longitud_corto { get; set; }
        public string bloque_cad_largo { get; set; }
        public string bloque_cad_corto { get; set; }
    }

    // --- NUEVA CLASE para almacenar resultados ---
    public class LayoutResult
    {
        public double OffsetEO { get; set; }
        public int TotalTrackers { get; set; }
        public int LongTrackers { get; set; }
        public int ShortTrackers { get; set; }
        public List<Polyline> TrackersToDraw { get; set; }
    }


    public class Phase1Commands
    {
        [CommandMethod("FASE1")]
        public static void RunPhase1()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            ed.WriteMessage("\n--- Iniciando FASE 1 (v29 - Optimización y Dibujado Final) ---");

            // --- PASO 1: Cargar Biblioteca de Trackers ---
            List<TrackerModel> trackerLibrary;
            string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string dllDirectory = Path.GetDirectoryName(dllPath);
            string jsonPath = Path.Combine(dllDirectory, "trackers.json");
            try
            {
                string jsonContent = File.ReadAllText(jsonPath);
                trackerLibrary = JsonConvert.DeserializeObject<List<TrackerModel>>(jsonContent);
                if (trackerLibrary == null || trackerLibrary.Count == 0) { /* ... error ... */ return; }
                ed.WriteMessage($"\nBiblioteca 'trackers.json' cargada. {trackerLibrary.Count} modelos encontrados.");
            }
            catch (System.Exception) { /* ... error ... */ return; }


            // --- PASO 2: Solicitar Inputs de Layout ---
            
            // 2a. Seleccionar Tracker
            PromptKeywordOptions pkoTracker = new PromptKeywordOptions("\nSeleccione el id_tracker de la biblioteca:");
            foreach (var tracker in trackerLibrary) { pkoTracker.Keywords.Add(tracker.id_tracker); }
            pkoTracker.Keywords.Default = trackerLibrary[0].id_tracker;
            PromptResult prTracker = ed.GetKeywords(pkoTracker);
            if (prTracker.Status != PromptStatus.OK) { return; }
            string selectedTrackerId = prTracker.StringResult;
            TrackerModel selectedTracker = trackerLibrary.Find(t => t.id_tracker == selectedTrackerId);
            ed.WriteMessage($"\nTracker '{selectedTracker.id_tracker}' seleccionado. (Ancho N-S: {selectedTracker.ancho_huella_ns}m)");

            // 2b. Pedir Paso Libre N-S
            PromptDoubleOptions pdoPaso = new PromptDoubleOptions("\nIntroduzca el paso libre N-S (distancia fin-a-inicio) en metros:");
            pdoPaso.AllowNegative = false; pdoPaso.AllowZero = false; pdoPaso.DefaultValue = 3.0;
            PromptDoubleResult prPaso = ed.GetDouble(pdoPaso);
            if (prPaso.Status != PromptStatus.OK) { return; }
            double pasoLibreNS = prPaso.Value;
            double pitchEjeAEje = selectedTracker.ancho_huella_ns + pasoLibreNS;
            ed.WriteMessage($"\nPaso libre N-S: {pasoLibreNS}m. Pitch N-S Eje-a-Eje calculado: {pitchEjeAEje}m");

            // 2c. Pedir Retranqueo (Setback)
            PromptDoubleOptions pdoSetback = new PromptDoubleOptions("\nIntroduzca el retranqueo (setback) de la parcela en metros:");
            pdoSetback.AllowNegative = false; pdoSetback.AllowZero = true; pdoSetback.DefaultValue = 5.0;
            PromptDoubleResult prSetback = ed.GetDouble(pdoSetback);
            if (prSetback.Status != PromptStatus.OK) { return; }
            double setback = prSetback.Value;
            ed.WriteMessage($"\nRetranqueo seleccionado: {setback}m");


            // --- PASO 3: Selección de Geometría ---
            
            // 3a. Seleccionar Parcela
            ObjectId parcelId = SelectPolyline(ed, "\nSeleccione la Polilínea de la Parcela:");
            if (parcelId == ObjectId.Null) { return; }
            ed.WriteMessage("\nParcela seleccionada.");

            // 3b. Seleccionar Afecciones
            ObjectIdCollection affectionIds = SelectMultiplePolylines(ed, "\nSeleccione las Polilíneas de Afecciones (o pulse Intro para ninguna):");
            ed.WriteMessage($"\n{affectionIds.Count} afecciones seleccionadas.");

            ed.WriteMessage("\n--- Todos los inputs han sido seleccionados. ---");


            // --- PASO 4: Cálculo de Área Neta y Mapa de Validez ---
            ed.WriteMessage("\nIniciando Paso 4: Cálculo del Área Neta...");
            
            // 4a. Calcular Retranqueo (Setback)
            ObjectId netAreaId = GetNetArea(db, parcelId, setback);
            if (netAreaId == ObjectId.Null) { ed.WriteMessage("\nERROR: No se pudo calcular el Área Neta (retranqueo). Cancelando."); return; }
            ed.WriteMessage("\n¡Área Neta (retranqueo) calculada y dibujada en 'AREA_NETA'!");

            // 4b. Restar Afecciones (Llamando a la v2 corregida)
            ObjectIdCollection finalValidAreaIds;
            if (affectionIds.Count > 0)
            {
                ed.WriteMessage("\nRestando afecciones del Área Neta...");
                // --- LLAMANDO A LA v2 CORREGIDA ---
                finalValidAreaIds = SubtractAffections_v2(db, netAreaId, affectionIds);
                if (finalValidAreaIds.Count == 0) { ed.WriteMessage("\nERROR: La resta de afecciones falló."); return; }
                ed.WriteMessage($"\n¡Afecciones restadas con éxito! Resultado dibujado en 'AREA_VALIDA_FINAL'.");
            }
            else
            {
                finalValidAreaIds = new ObjectIdCollection { netAreaId };
            }
            
            ed.WriteMessage("\n¡Mapa de Validez (Polilíneas) calculado con éxito!");


            // --- PASO 5: Bucle de Optimización ---
            ed.WriteMessage("\n--- Iniciando Paso 5: Bucle de Optimización (100 iteraciones) ---");
            LayoutResult winningLayout = RunOptimizationLoop(db, finalValidAreaIds, selectedTracker, pitchEjeAEje);
            
            if (winningLayout == null)
            {
                ed.WriteMessage("\nERROR: No se encontró ningún layout válido.");
                return;
            }
            ed.WriteMessage("--- Bucle de Optimización Terminado ---");
            ed.WriteMessage("\n--- LAYOUT GANADOR SELECCIONADO ---");
            ed.WriteMessage($"Offset (E-O): {winningLayout.OffsetEO:F2}m");
            ed.WriteMessage($"Total Trackers: {winningLayout.TotalTrackers}");
            ed.WriteMessage($"Trackers Largos ({selectedTracker.longitud_largo}m): {winningLayout.LongTrackers}");
            ed.WriteMessage($"Trackers Cortos ({selectedTracker.longitud_corto}m): {winningLayout.ShortTrackers}");


            // --- PASO 6: Dibujado Final ---
            ed.WriteMessage("\n--- Iniciando Paso 6: Dibujando Layout Ganador ---");
            DrawFinalLayout(db, winningLayout);
            ed.WriteMessage("\n¡Trackers dibujados con éxito en capas 'TRACKERS_LARGOS' y 'TRACKERS_CORTOS'!");

            ed.WriteMessage("\n--- PROCESO FASE 1 TERMINADO ---");
        }

        // --- Función Auxiliar 1 (Sin cambios) ---
        private static ObjectId SelectPolyline(Editor ed, string message)
        {
            PromptEntityOptions peo = new PromptEntityOptions(message);
            peo.SetRejectMessage("\nEl objeto seleccionado no es una Polilínea.");
            peo.AddAllowedClass(typeof(Polyline), true); peo.AddAllowedClass(typeof(Polyline2d), true); peo.AddAllowedClass(typeof(Polyline3d), true);
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status == PromptStatus.OK) { return per.ObjectId; }
            return ObjectId.Null;
        }

        // --- Función Auxiliar 2 (Sin cambios) ---
        private static ObjectIdCollection SelectMultiplePolylines(Editor ed, string message)
        {
            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = message; pso.MessageForRemoval = "\nEliminar objetos de la selección:";
            TypedValue[] filter = new TypedValue[] { new TypedValue((int)DxfCode.Operator, "<OR"), new TypedValue((int)DxfCode.Start, "POLYLINE"), new TypedValue((int)DxfCode.Start, "LWPOLYLINE"), new TypedValue((int)DxfCode.Start, "POLYLINE2D"), new TypedValue((int)DxfCode.Start, "POLYLINE3D"), new TypedValue((int)DxfCode.Operator, "OR>") };
            SelectionFilter selFilter = new SelectionFilter(filter);
            PromptSelectionResult psr = ed.GetSelection(pso, selFilter);
            if (psr.Status == PromptStatus.OK) { return new ObjectIdCollection(psr.Value.GetObjectIds()); }
            return new ObjectIdCollection(); 
        }

        // --- FUNCIÓN ELIMINADA ---
        // private static void DrawTestTrackers(Database db, TrackerModel tracker) { ... }

        // --- Función Auxiliar 4 (CreateLayer, sin cambios) ---
        private static void CreateLayer(Database db, Transaction tr, string layerName, Color color)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();
                LayerTableRecord ltr = new LayerTableRecord();
                ltr.Name = layerName; ltr.Color = color;
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }

        // --- Función Auxiliar 5 (GetNetArea, sin cambios) ---
        private static ObjectId GetNetArea(Database db, ObjectId parcelId, double setback)
        {
            if (setback == 0) return parcelId;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Curve curve = tr.GetObject(parcelId, OpenMode.ForRead) as Curve;
                if (curve == null) { return ObjectId.Null; }
                double originalArea = curve.Area;
                string layerName = "AREA_NETA";
                Color color = Color.FromRgb(255, 0, 255); // Magenta
                CreateLayer(db, tr, layerName, color);
                DBObjectCollection offsetCurves = null;
                try { offsetCurves = curve.GetOffsetCurves(-setback); } catch (System.Exception) { return ObjectId.Null; }
                if (offsetCurves != null && offsetCurves.Count > 0)
                {
                    Curve offsetCurve = offsetCurves[0] as Curve;
                    if (offsetCurve != null && offsetCurve.Area < originalArea)
                    {
                        return AddCurveToModelSpace(db, tr, offsetCurve, layerName);
                    }
                }
                try { offsetCurves = curve.GetOffsetCurves(setback); } catch (System.Exception) { return ObjectId.Null; }
                if (offsetCurves != null && offsetCurves.Count > 0)
                {
                     Curve offsetCurve = offsetCurves[0] as Curve;
                     if (offsetCurve != null && offsetCurve.Area < originalArea)
                     {
                        return AddCurveToModelSpace(db, tr, offsetCurve, layerName);
                     }
                }
                return ObjectId.Null;
            }
        }

        // --- Función Auxiliar 6 (Renombrada de AddNetAreaToModelSpace) ---
        private static ObjectId AddCurveToModelSpace(Database db, Transaction tr, Curve curve, string layerName)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            Entity ent = curve as Entity;
            ent.Layer = layerName;
            btr.AppendEntity(ent);
            tr.AddNewlyCreatedDBObject(ent, true);
            tr.Commit();
            return ent.ObjectId;
        }

        // --- FUNCIÓN 'SubtractAffections' (v2 - CORREGIDA) ---
        /// <summary>
        /// Resta afecciones y EXPORTA el resultado a POLILÍNEAS.
        /// </summary>
        private static ObjectIdCollection SubtractAffections_v2(Database db, ObjectId netAreaId, ObjectIdCollection affectionIds)
        {
            ObjectIdCollection finalAreaIds = new ObjectIdCollection();
            string layerName = "AREA_VALIDA_FINAL";
            Color color = Color.FromRgb(0, 255, 0); // Verde
            List<Region> regionsToDispose = new List<Region>(); // Para limpiar memoria

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    CreateLayer(db, tr, layerName, color);
                    Curve netAreaCurve = tr.GetObject(netAreaId, OpenMode.ForRead) as Curve;
                    if (netAreaCurve == null) return finalAreaIds;
                    
                    DBObjectCollection netAreaRegions = Region.CreateFromCurves(new DBObjectCollection { netAreaCurve });
                    if (netAreaRegions.Count == 0) return finalAreaIds;
                    Region baseRegion = netAreaRegions[0] as Region;
                    regionsToDispose.Add(baseRegion);

                    foreach (ObjectId affId in affectionIds)
                    {
                        Curve affCurve = tr.GetObject(affId, OpenMode.ForRead) as Curve;
                        if (affCurve == null) continue;
                        DBObjectCollection affRegions = Region.CreateFromCurves(new DBObjectCollection { affCurve });
                        if (affRegions.Count > 0)
                        {
                            Region affRegion = affRegions[0] as Region;
                            regionsToDispose.Add(affRegion); // Añadir para limpiar
                            baseRegion.BooleanOperation(BooleanOperationType.BoolSubtract, affRegion);
                        }
                    }

                    // --- LÓGICA DE EXPLOSIÓN ---
                    // En lugar de guardar la Región, la explotamos de nuevo a Polilíneas
                    if (!baseRegion.IsDisposed && baseRegion.Area > 0.001)
                    {
                        DBObjectCollection explodedObjects = new DBObjectCollection();
                        baseRegion.Explode(explodedObjects); // <--- EXPLOTAR

                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                        foreach (DBObject obj in explodedObjects)
                        {
                            Entity ent = obj as Entity;
                            if (ent != null)
                            {
                                ent.Layer = layerName; // Poner en capa verde
                                btr.AppendEntity(ent);
                                tr.AddNewlyCreatedDBObject(ent, true);
                                finalAreaIds.Add(ent.ObjectId); // Guardar el ID de la Polilínea
                            }
                        }
                    }

                    tr.Commit();
                }
                catch (System.Exception ex)
                {
                    Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\nERROR durante la resta booleana v2: {ex.Message}");
                    tr.Abort();
                }
                finally
                {
                    // Limpiar todas las regiones temporales
                    foreach (Region r in regionsToDispose) { r.Dispose(); }
                }
                
                return finalAreaIds;
            }
        }

        // --- NUEVA FUNCIÓN (Paso 5) ---
        /// <summary>
        /// Ejecuta el bucle de optimización para encontrar el mejor Offset E-O.
        /// </summary>
        private static LayoutResult RunOptimizationLoop(Database db, ObjectIdCollection validAreaIds, TrackerModel tracker, double pitchNS)
        {
            LayoutResult bestLayout = new LayoutResult { TotalTrackers = 0 };

            // 1. Abrir las polilíneas del área válida para leerlas
            List<Polyline> validPolylines = new List<Polyline>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in validAreaIds)
                {
                    Polyline poly = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                    if (poly != null) validPolylines.Add(poly);
                }
                tr.Abort(); // Solo leemos, no guardamos cambios
            }
            if (validPolylines.Count == 0) return null; // No hay área válida

            // 2. Obtener la extensión total de todas las polilíneas
            Extents3d totalExtents = validPolylines[0].GeometricExtents;
            for(int i=1; i < validPolylines.Count; i++)
            {
                totalExtents.AddExtents(validPolylines[i].GeometricExtents);
            }

            // 3. El Bucle de 100 Iteraciones
            // Probaremos 100 offsets E-O, de 0.1m a 10.0m
            for (int i = 0; i < 100; i++)
            {
                double currentOffsetEO = 0.1 + (i * 0.1); // de 0.1 a 10.0
                LayoutResult currentLayout = new LayoutResult 
                { 
                    OffsetEO = currentOffsetEO, 
                    TrackersToDraw = new List<Polyline>() 
                };

                // 4. Iterar la Grilla
                // Bucle N-S (Y)
                for (double y = totalExtents.MinPoint.Y; y < totalExtents.MaxPoint.Y; y += pitchNS)
                {
                    // Bucle E-O (X)
                    double x = totalExtents.MinPoint.X;
                    while (x < totalExtents.MaxPoint.X)
                    {
                        // 5. Probar Tracker Largo
                        Point3d centerPt = new Point3d(x + (tracker.longitud_largo / 2.0), y + (tracker.ancho_huella_ns / 2.0), 0);
                        
                        // 6. Test de Colisión (simple: solo centro)
                        if (IsPointInside(validPolylines, centerPt))
                        {
                            currentLayout.LongTrackers++;
                            currentLayout.TrackersToDraw.Add(CreateTrackerPolyline(centerPt, tracker.longitud_largo, tracker.ancho_huella_ns, "TRACKERS_LARGOS"));
                            x += tracker.longitud_largo + currentOffsetEO; // Moverse al siguiente
                        }
                        else
                        {
                            // 7. Si el largo no cabe, probar corto (si existe)
                            if (tracker.longitud_corto > 0.01)
                            {
                                centerPt = new Point3d(x + (tracker.longitud_corto / 2.0), y + (tracker.ancho_huella_ns / 2.0), 0);
                                if (IsPointInside(validPolylines, centerPt))
                                {
                                    currentLayout.ShortTrackers++;
                                    currentLayout.TrackersToDraw.Add(CreateTrackerPolyline(centerPt, tracker.longitud_corto, tracker.ancho_huella_ns, "TRACKERS_CORTOS"));
                                    x += tracker.longitud_corto + currentOffsetEO;
                                }
                                else
                                {
                                    x += 1.0; // Moverse 1m e re-intentar (para saltar zonas vacías)
                                }
                            }
                            else
                            {
                                 x += 1.0; // Moverse 1m e re-intentar
                            }
                        }
                    }
                }

                // 8. Guardar el mejor resultado
                currentLayout.TotalTrackers = currentLayout.LongTrackers + currentLayout.ShortTrackers;
                if (currentLayout.TotalTrackers > bestLayout.TotalTrackers)
                {
                    bestLayout = currentLayout;
                }
            }
            
            return bestLayout;
        }

        // --- NUEVA FUNCIÓN (Test de Colisión) ---
        /// <summary>
        /// Comprueba si un punto está dentro de CUALQUIERA de las polilíneas de la lista.
        /// Usa un algoritmo Ray-Casting.
        /// </summary>
        private static bool IsPointInside(List<Polyline> polylines, Point3d testPoint)
        {
            foreach (Polyline poly in polylines)
            {
                if (IsPointInsideSinglePoly(poly, testPoint))
                {
                    return true; // Está dentro de al menos una
                }
            }
            return false; // No está en ninguna
        }

        private static bool IsPointInsideSinglePoly(Polyline poly, Point3d testPoint)
        {
            // Algoritmo Ray-Casting
            int crossings = 0;
            for (int i = 0; i < poly.NumberOfVertices; i++)
            {
                Point3d p1 = poly.GetPoint3dAt(i);
                Point3d p2 = poly.GetPoint3dAt((i + 1) % poly.NumberOfVertices); // Siguiente o el primero

                if (((p1.Y <= testPoint.Y && p2.Y > testPoint.Y) || (p1.Y > testPoint.Y && p2.Y <= testPoint.Y)) &&
                    (testPoint.X < (p2.X - p1.X) * (testPoint.Y - p1.Y) / (p2.Y - p1.Y) + p1.X))
                {
                    crossings++;
                }
            }
            return (crossings % 2 == 1); // Impar = Dentro
        }

        // --- NUEVA FUNCIÓN (Crear Geometría de Tracker) ---
        /// <summary>
        /// Crea una polilínea de rectángulo para un tracker, pero NO la añade al dibujo.
        /// </summary>
        private static Polyline CreateTrackerPolyline(Point3d center, double length, double width, string layer)
        {
            double halfLen = length / 2.0;
            double halfWid = width / 2.0;
            
            Polyline rect = new Polyline();
            rect.SetDatabaseDefaults();
            rect.Layer = layer;
            
            rect.AddVertexAt(0, new Point2d(center.X - halfLen, center.Y - halfWid), 0, 0, 0);
            rect.AddVertexAt(1, new Point2d(center.X + halfLen, center.Y - halfWid), 0, 0, 0);
            rect.AddVertexAt(2, new Point2d(center.X + halfLen, center.Y + halfWid), 0, 0, 0);
            rect.AddVertexAt(3, new Point2d(center.X - halfLen, center.Y + halfWid), 0, 0, 0);
            rect.Closed = true;

            return rect;
        }
        
        // --- NUEVA FUNCIÓN (Paso 6) ---
        /// <summary>
        /// Dibuja el layout ganador en el ModelSpace.
        /// </summary>
        private static void DrawFinalLayout(Database db, LayoutResult winningLayout)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // Crear capas
                CreateLayer(db, tr, "TRACKERS_LARGOS", Color.FromRgb(0, 100, 255)); // Azul
                CreateLayer(db, tr, "TRACKERS_CORTOS", Color.FromRgb(255, 100, 0)); // Naranja

                // Dibujar cada tracker del layout ganador
                foreach (Polyline trackerPoly in winningLayout.TrackersToDraw)
                {
                    // Añadimos la polilínea (que ya tiene capa) al ModelSpace
                    btr.AppendEntity(trackerPoly);
                    tr.AddNewlyCreatedDBObject(trackerPoly, true);
                }

                tr.Commit();
            }
        }
    }
}
