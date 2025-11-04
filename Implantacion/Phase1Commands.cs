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
// NO SE NECESITAN LIBRERÍAS EXTRA

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

    public class LayoutResult
    {
        // ... (Esta clase no cambia) ...
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

            // --- CAMBIO DE VERSIÓN ---
            ed.WriteMessage("\n--- Iniciando FASE 1 (v35 - Optimización por Ray-Casting) ---");

            // --- PASO 1: Cargar Biblioteca de Trackers (Sin cambios) ---
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


            // --- PASO 2: Solicitar Inputs de Layout (Sin cambios) ---
            
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


            // --- PASO 3: Selección de Geometría (Sin cambios) ---
            
            // 3a. Seleccionar Parcela
            ObjectId parcelId = SelectPolyline(ed, "\nSeleccione la Polilínea de la Parcela:");
            if (parcelId == ObjectId.Null) { return; }
            ed.WriteMessage("\nParcela seleccionada.");

            // 3b. Seleccionar Afecciones
            ObjectIdCollection affectionIds = SelectMultiplePolylines(ed, "\nSeleccione las Polilíneas de Afecciones (o pulse Intro para ninguna):");
            ed.WriteMessage($"\n{affectionIds.Count} afecciones seleccionadas.");

            ed.WriteMessage("\n--- Todos los inputs han sido seleccionados. ---");


            // --- PASO 4: Cálculo de Área Neta (Simplificado) ---
            ed.WriteMessage("\nIniciando Paso 4: Cálculo del Área Neta...");
            
            // 4a. Calcular Retranqueo (Setback)
            ObjectId netAreaId = GetNetArea(db, parcelId, setback);
            if (netAreaId == ObjectId.Null) { ed.WriteMessage("\nERROR: No se pudo calcular el Área Neta (retranqueo). Cancelando."); return; }
            ed.WriteMessage("\n¡Área Neta (retranqueo) calculada y dibujada en 'AREA_NETA'!");
            
            // --- LÓGICA DE RESTA BOOLEANA ELIMINADA ---
            ed.WriteMessage("\n¡Mapa de Validez (Polilíneas) calculado con éxito!");


            // --- PASO 5: Bucle de Optimización (v35) ---
            ed.WriteMessage("\n--- Iniciando Paso 5: Bucle de Optimización (100 iteraciones) ---");
            
            // --- LLAMADA A LA NUEVA FUNCIÓN ---
            LayoutResult winningLayout = RunOptimizationLoop_v35(db, netAreaId, affectionIds, selectedTracker, pitchEjeAEje);
            
            if (winningLayout == null)
            {
                ed.WriteMessage("\nERROR: No se encontró ningún layout válido (Área Cero?).");
                return;
            }
            
            if (winningLayout.TotalTrackers == 0)
            {
                ed.WriteMessage("\nAVISO: La optimización se completó, pero no caben trackers en el área válida.");
                return;
            }

            ed.WriteMessage("--- Bucle de Optimización Terminado ---");
            ed.WriteMessage("\n--- LAYOUT GANADOR SELECCIONADO ---");
            ed.WriteMessage($"Offset (E-O): {winningLayout.OffsetEO:F2}m");
            ed.WriteMessage($"Total Trackers: {winningLayout.TotalTrackers}");
            ed.WriteMessage($"Trackers Largos ({selectedTracker.longitud_largo}m): {winningLayout.LongTrackers}");
            ed.WriteMessage($"Trackers Cortos ({selectedTracker.longitud_corto}m): {winningLayout.ShortTrackers}");


            // --- PASO 6: Dibujado Final (Sin cambios) ---
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

        // --- Función Auxiliar 6 (AddCurveToModelSpace, sin cambios) ---
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


        // --- FUNCIONES DE REGIÓN ELIMINADAS ---
        // (SubtractAffections_v30)
        // (ConvertCurveToRegion)


        // --- FUNCIÓN 'RunOptimizationLoop' (v35 - REESCRITA) ---
        private static LayoutResult RunOptimizationLoop_v35(Database db, ObjectId netAreaId, ObjectIdCollection affectionIds, TrackerModel tracker, double pitchNS)
        {
            LayoutResult bestLayout = new LayoutResult { TotalTrackers = 0 };

            // 1. Abrir las Polilíneas de área neta y afecciones
            Polyline netAreaPoly = null;
            List<Polyline> affectionPolys = new List<Polyline>();
            Extents3d totalExtents = new Extents3d();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Abrir la polilínea magenta
                netAreaPoly = tr.GetObject(netAreaId, OpenMode.ForRead) as Polyline;
                if (netAreaPoly == null) return null; // Error grave
                totalExtents = netAreaPoly.GeometricExtents;
                
                // Abrir todas las polilíneas de afecciones
                foreach (ObjectId id in affectionIds)
                {
                    Polyline poly = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                    if (poly != null) affectionPolys.Add(poly);
                }
                tr.Abort(); // Solo leemos
            } 

            // 2. El Bucle de 100 Iteraciones
            for (int i = 0; i < 100; i++)
            {
                double currentOffsetEO = 0.1 + (i * 0.1); 
                LayoutResult currentLayout = new LayoutResult 
                { 
                    OffsetEO = currentOffsetEO, 
                    TrackersToDraw = new List<Polyline>() 
                };

                // 3. Iterar la Grilla
                for (double y = totalExtents.MinPoint.Y; y < totalExtents.MaxPoint.Y; y += pitchNS)
                {
                    double x = totalExtents.MinPoint.X;
                    while (x < totalExtents.MaxPoint.X)
                    {
                        // 4. Probar Tracker Largo
                        Point3d centerPt = new Point3d(x + (tracker.longitud_largo / 2.0), y + (tracker.ancho_huella_ns / 2.0), 0);
                        
                        // 5. Test de Colisión (v35)
                        if (IsPointValid(netAreaPoly, affectionPolys, centerPt))
                        {
                            currentLayout.LongTrackers++;
                            currentLayout.TrackersToDraw.Add(CreateTrackerPolyline(centerPt, tracker.longitud_largo, tracker.ancho_huella_ns, "TRACKERS_LARGOS"));
                            x += tracker.longitud_largo + currentOffsetEO;
                        }
                        else
                        {
                            // 6. Si el largo no cabe, probar corto
                            if (tracker.longitud_corto > 0.01)
                            {
                                centerPt = new Point3d(x + (tracker.longitud_corto / 2.0), y + (tracker.ancho_huella_ns / 2.0), 0);
                                if (IsPointValid(netAreaPoly, affectionPolys, centerPt))
                                {
                                    currentLayout.ShortTrackers++;
                                    currentLayout.TrackersToDraw.Add(CreateTrackerPolyline(centerPt, tracker.longitud_corto, tracker.ancho_huella_ns, "TRACKERS_CORTOS"));
                                    x += tracker.longitud_corto + currentOffsetEO;
                                }
                                else { x += 1.0; } // Avanzar 1m
                            }
                            else { x += 1.0; } // Avanzar 1m
                        }
                    }
                }

                // 7. Guardar el mejor resultado
                currentLayout.TotalTrackers = currentLayout.LongTrackers + currentLayout.ShortTrackers;
                if (currentLayout.TotalTrackers > bestLayout.TotalTrackers)
                {
                    bestLayout = currentLayout;
                }
            }
            
            return bestLayout;
        }

        // --- FUNCIÓN 'IsPointInsideRegions' REEMPLAZADA POR 'IsPointValid' (v35) ---
        /// <summary>
        /// Comprueba si un punto es válido (Dentro de Área Neta Y Fuera de Afecciones)
        /// </summary>
        private static bool IsPointValid(Polyline netArea, List<Polyline> affections, Point3d testPoint)
        {
            // Condición 1: Debe estar DENTRO del área neta
            if (!IsPointInsidePoly(netArea, testPoint))
            {
                return false;
            }
            
            // Condición 2: NO debe estar dentro de NINGUNA afección
            foreach (Polyline affPoly in affections)
            {
                if (IsPointInsidePoly(affPoly, testPoint))
                {
                    return false; // Está bloqueado por una afección
                }
            }
            
            return true; // Pasó ambas pruebas
        }
        
        /// <summary>
        /// Algoritmo Ray-Casting para comprobar si un punto 2D está en una Polilínea 2D.
        /// </summary>
        private static bool IsPointInsidePoly(Polyline poly, Point3d testPoint)
        {
            int crossings = 0;
            for (int i = 0; i < poly.NumberOfVertices; i++)
            {
                // Obtener Puntos 2D
                Point2d p1 = poly.GetPoint2dAt(i);
                Point2d p2 = poly.GetPoint2dAt((i + 1) % poly.NumberOfVertices); // Siguiente o el primero
                Point2d test = testPoint.Convert2d(new Plane());


                if (((p1.Y <= test.Y && p2.Y > test.Y) || (p1.Y > test.Y && p2.Y <= test.Y)) &&
                    (test.X < (p2.X - p1.X) * (test.Y - p1.Y) / (p2.Y - p1.Y) + p1.X))
                {
                    crossings++;
                }
            }
            return (crossings % 2 == 1); // Impar = Dentro
        }


        // --- CreateTrackerPolyline (sin cambios) ---
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
        
        // --- DrawFinalLayout (sin cambios) ---
        private static void DrawFinalLayout(Database db, LayoutResult winningLayout)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                CreateLayer(db, tr, "TRACKERS_LARGOS", Color.FromRgb(0, 100, 255));
                CreateLayer(db, tr, "TRACKERS_CORTOS", Color.FromRgb(255, 100, 0)); 
                foreach (Polyline trackerPoly in winningLayout.TrackersToDraw)
                {
                    btr.AppendEntity(trackerPoly);
                    tr.AddNewlyCreatedDBObject(trackerPoly, true);
                }
                tr.Commit();
            }
        }
    }
}
