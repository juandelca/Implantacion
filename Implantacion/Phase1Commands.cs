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
            ed.WriteMessage("\n--- Iniciando FASE 1 (v32 - Corrección final .Contains) ---");

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

            // 4b. Restar Afecciones (Llamando a la v30)
            ObjectIdCollection finalValidAreaIds; // <-- CONTENDRÁ IDs DE REGIONES
            if (affectionIds.Count > 0)
            {
                ed.WriteMessage("\nRestando afecciones del Área Neta...");
                finalValidAreaIds = SubtractAffections_v30(db, netAreaId, affectionIds);
                if (finalValidAreaIds.Count == 0) { ed.WriteMessage("\nERROR: La resta de afecciones falló."); return; }
                ed.WriteMessage($"\n¡Afecciones restadas con éxito! Resultado dibujado en 'AREA_VALIDA_FINAL'.");
            }
            else
            {
                // Si no hay afecciones, convertir la polilínea 'netAreaId' a Región
                finalValidAreaIds = ConvertCurveToRegion(db, netAreaId, "AREA_VALIDA_FINAL", Color.FromRgb(0, 255, 0));
                if (finalValidAreaIds.Count == 0) { ed.WriteMessage("\nERROR: No se pudo convertir el área neta a región."); return; }
            }
            
            ed.WriteMessage("\n¡Mapa de Validez (REGIONES) calculado con éxito!");


            // --- PASO 5: Bucle de Optimización ---
            ed.WriteMessage("\n--- Iniciando Paso 5: Bucle de Optimización (100 iteraciones) ---");
            LayoutResult winningLayout = RunOptimizationLoop_v30(db, finalValidAreaIds, selectedTracker, pitchEjeAEje);
            
            if (winningLayout == null)
            {
                ed.WriteMessage("\nERROR: No se encontró ningún layout válido (Región Cero?).");
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


        // --- FUNCIÓN 'SubtractAffections_v30' (Sin cambios) ---
        private static ObjectIdCollection SubtractAffections_v30(Database db, ObjectId netAreaId, ObjectIdCollection affectionIds)
        {
            ObjectIdCollection finalAreaIds = new ObjectIdCollection();
            string layerName = "AREA_VALIDA_FINAL";
            Color color = Color.FromRgb(0, 255, 0); // Verde
            List<Region> regionsToDispose = new List<Region>();

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
                            regionsToDispose.Add(affRegion); 
                            baseRegion.BooleanOperation(BooleanOperationType.BoolSubtract, affRegion);
                        }
                    }

                    if (!baseRegion.IsDisposed && baseRegion.Area > 0.001)
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                        
                        baseRegion.Layer = layerName;
                        btr.AppendEntity(baseRegion);
                        tr.AddNewlyCreatedDBObject(baseRegion, true);
                        
                        finalAreaIds.Add(baseRegion.ObjectId);
                    }

                    tr.Commit();
                }
                catch (System.Exception ex)
                {
                    Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\nERROR durante la resta booleana v30: {ex.Message}");
                    tr.Abort();
                }
                finally
                {
                    foreach (Region r in regionsToDispose) 
                    { 
                        if (r != null && !r.IsDisposed && !r.IsReadEnabled && !r.IsReadEnabled) 
                            r.Dispose(); 
                    }
                }
                
                return finalAreaIds;
            }
        }
        
        // --- ConvertCurveToRegion (Sin cambios) ---
        private static ObjectIdCollection ConvertCurveToRegion(Database db, ObjectId curveId, string layerName, Color color)
        {
             using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    CreateLayer(db, tr, layerName, color);
                    Curve curve = tr.GetObject(curveId, OpenMode.ForRead) as Curve;
                    if (curve == null) return new ObjectIdCollection();

                    DBObjectCollection regions = Region.CreateFromCurves(new DBObjectCollection { curve });
                    if (regions.Count == 0) return new ObjectIdCollection();
                    
                    Region region = regions[0] as Region;
                    
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    region.Layer = layerName;
                    btr.AppendEntity(region);
                    tr.AddNewlyCreatedDBObject(region, true);

                    tr.Commit();
                    return new ObjectIdCollection { region.ObjectId };
                }
                catch { tr.Abort(); return new ObjectIdCollection(); }
            }
        }


        // --- RunOptimizationLoop_v30 (Sin cambios) ---
        private static LayoutResult RunOptimizationLoop_v30(Database db, ObjectIdCollection validRegionIds, TrackerModel tracker, double pitchNS)
        {
            LayoutResult bestLayout = new LayoutResult { TotalTrackers = 0 };

            List<Region> validRegions = new List<Region>();
            Extents3d totalExtents = new Extents3d();
            bool extentsInitialized = false;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in validRegionIds)
                {
                    Region region = tr.GetObject(id, OpenMode.ForRead) as Region;
                    if (region != null)
                    { 
                        validRegions.Add(region);
                        if (!extentsInitialized)
                        {
                            totalExtents = region.GeometricExtents;
                            extentsInitialized = true;
                        }
                        else
                        {
                            totalExtents.AddExtents(region.GeometricExtents);
                        }
                    }
                }
            } 

            if (validRegions.Count == 0) return null; 

            for (int i = 0; i < 100; i++)
            {
                double currentOffsetEO = 0.1 + (i * 0.1); 
                LayoutResult currentLayout = new LayoutResult 
                { 
                    OffsetEO = currentOffsetEO, 
                    TrackersToDraw = new List<Polyline>() 
                };

                for (double y = totalExtents.MinPoint.Y; y < totalExtents.MaxPoint.Y; y += pitchNS)
                {
                    double x = totalExtents.MinPoint.X;
                    while (x < totalExtents.MaxPoint.X)
                    {
                        Point3d centerPt = new Point3d(x + (tracker.longitud_largo / 2.0), y + (tracker.ancho_huella_ns / 2.0), 0);
                        
                        if (IsPointInsideRegions(db, validRegionIds, centerPt))
                        {
                            currentLayout.LongTrackers++;
                            currentLayout.TrackersToDraw.Add(CreateTrackerPolyline(centerPt, tracker.longitud_largo, tracker.ancho_huella_ns, "TRACKERS_LARGOS"));
                            x += tracker.longitud_largo + currentOffsetEO;
                        }
                        else
                        {
                            if (tracker.longitud_corto > 0.01)
                            {
                                centerPt = new Point3d(x + (tracker.longitud_corto / 2.0), y + (tracker.ancho_huella_ns / 2.0), 0);
                                if (IsPointInsideRegions(db, validRegionIds, centerPt))
                                {
                                    currentLayout.ShortTrackers++;
                                    currentLayout.TrackersToDraw.Add(CreateTrackerPolyline(centerPt, tracker.longitud_corto, tracker.ancho_huella_ns, "TRACKERS_CORTOS"));
                                    x += tracker.longitud_corto + currentOffsetEO;
                                }
                                else { x += 1.0; }
                            }
                            else { x += 1.0; }
                        }
                    }
                }

                currentLayout.TotalTrackers = currentLayout.LongTrackers + currentLayout.ShortTrackers;
                if (currentLayout.TotalTrackers > bestLayout.TotalTrackers)
                {
                    bestLayout = currentLayout;
                }
            }
            
            return bestLayout;
        }

        // --- FUNCIÓN 'IsPointInsideRegions' (v32 - CORREGIDA) ---
        /// <summary>
        /// Comprueba si un punto está dentro de CUALQUIERA de las REGIONES.
        /// </summary>
        private static bool IsPointInsideRegions(Database db, ObjectIdCollection regionIds, Point3d testPoint)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in regionIds)
                {
                    Region region = tr.GetObject(id, OpenMode.ForRead) as Region;
                    if (region != null)
                    {
                        // --- CORRECCIÓN v32: De .PointInRegion a .Contains ---
                        // Este era el método correcto de la v30, que ahora
                        // debería funcionar gracias a la corrección de plataforma x64.
                        if (region.Contains(testPoint))
                        {
                            tr.Abort(); // Encontrado, no necesitamos más
                            return true;
                        }
                    }
                }
                tr.Abort(); // No encontrado
                return false;
            }
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
