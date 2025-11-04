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
            ed.WriteMessage("\n--- Iniciando FASE 1 (v43 - Corrección Typo Final) ---");

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


            // --- PASO 2: Solicitar Inputs de Layout (Sin cambios, v37) ---
            
            // 2a. Seleccionar Tracker
            PromptKeywordOptions pkoTracker = new PromptKeywordOptions("\nSeleccione el id_tracker de la biblioteca:");
            foreach (var tracker in trackerLibrary) { pkoTracker.Keywords.Add(tracker.id_tracker); }
            pkoTracker.Keywords.Default = trackerLibrary[0].id_tracker;
            PromptResult prTracker = ed.GetKeywords(pkoTracker);
            if (prTracker.Status != PromptStatus.OK) { return; }
            string selectedTrackerId = prTracker.StringResult;
            TrackerModel selectedTracker = trackerLibrary.Find(t => t.id_tracker == selectedTrackerId);
            ed.WriteMessage($"\nTracker '{selectedTracker.id_tracker}' seleccionado. (Ancho E-O: {selectedTracker.ancho_huella_ns}m)");

            // 2b. Pedir Pitch Eje-a-Eje (E-O)
            PromptDoubleOptions pdoPitch = new PromptDoubleOptions("\nIntroduzca el Pitch Eje-a-Eje (E-O) en metros:");
            pdoPitch.AllowNegative = false; pdoPitch.AllowZero = false; pdoPitch.DefaultValue = 10.0; 
            PromptDoubleResult prPitch = ed.GetDouble(pdoPitch);
            if (prPitch.Status != PromptStatus.OK) { return; }
            double pitchEO = prPitch.Value;
            ed.WriteMessage($"\nPitch E-O seleccionado: {pitchEO}m");

            // 2c. Pedir Retranqueo (Setback)
            PromptDoubleOptions pdoSetback = new PromptDoubleOptions("\nIntroduzca el retranqueo (setback) de la parcela en metros:");
            pdoSetback.AllowNegative = false; pdoSetback.AllowZero = true; pdoSetback.DefaultValue = 5.0;
            PromptDoubleResult prSetback = ed.GetDouble(pdoSetback);
            if (prSetback.Status != PromptStatus.OK) { return; }
            double setback = prSetback.Value;
            ed.WriteMessage($"\nRetranqueo seleccionado: {setback}m");


            // --- PASO 3: Selección de Geometría (Sin cambios, v41) ---
            
            // 3a. Seleccionar Parcela
            ObjectId parcelId = SelectPolyline(ed, "\nSeleccione la Polilínea de la Parcela (Debe ser 2D):");
            if (parcelId == ObjectId.Null) { return; }
            ed.WriteMessage("\nParcela seleccionada.");

            // 3b. Seleccionar Afecciones
            ObjectIdCollection affectionIds = SelectMultiplePolylines(ed, "\nSeleccione las Polilíneas de Afecciones (Deben ser 2D):");
            ed.WriteMessage($"\n{affectionIds.Count} afecciones seleccionadas.");

            ed.WriteMessage("\n--- Todos los inputs han sido seleccionados. ---");


            // --- PASO 4: Cálculo de Área Neta (Sin cambios) ---
            ed.WriteMessage("\nIniciando Paso 4: Cálculo del Área Neta...");
            
            // 4a. Calcular Retranqueo (Setback)
            ObjectId netAreaId = GetNetArea(db, parcelId, setback);
            if (netAreaId == ObjectId.Null) { ed.WriteMessage("\nERROR: No se pudo calcular el Área Neta (retranqueo). Cancelando."); return; }
            ed.WriteMessage("\n¡Área Neta (retranqueo) calculada y dibujada en 'AREA_NETA'!");
            
            ed.WriteMessage("\n¡Mapa de Validez (Polilíneas) calculado con éxito!");


            // --- PASO 5: Bucle de Optimización (Sin cambios, v37) ---
            
            ed.WriteMessage("\nCreando capas de salida 'TRACKERS_LARGOS' y 'TRACKERS_CORTOS'...");
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                CreateLayer(db, tr, "TRACKERS_LARGOS", Color.FromRgb(0, 100, 255)); // Azul
                CreateLayer(db, tr, "TRACKERS_CORTOS", Color.FromRgb(255, 100, 0)); // Naranja
                tr.Commit();
            }

            ed.WriteMessage("\n--- Iniciando Paso 5: Bucle de Optimización (100 iteraciones) ---");
            
            LayoutResult winningLayout = RunOptimizationLoop_v37(db, netAreaId, affectionIds, selectedTracker, pitchEO);
            
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
            ed.WriteMessage($"Offset (N-S): {winningLayout.OffsetEO:F2}m"); 
            ed.WriteMessage($"Total Trackers: {winningLayout.TotalTrackers}");
            ed.WriteMessage($"Trackers Largos ({selectedTracker.longitud_largo}m): {winningLayout.LongTrackers}");
            ed.WriteMessage($"Trackers Cortos ({selectedTracker.longitud_corto}m): {winningLayout.ShortTrackers}");


            // --- PASO 6: Dibujado Final (Sin cambios) ---
            ed.WriteMessage("\n--- Iniciando Paso 6: Dibujando Layout Ganador ---");
            DrawFinalLayout(db, winningLayout);
            ed.WriteMessage("\n¡Trackers dibujados con éxito en capas 'TRACKERS_LARGOS' y 'TRACKERS_CORTOS'!");

            ed.WriteMessage("\n--- PROCESO FASE 1 TERMINADO ---");
        }

        // --- Función Auxiliar 1 (Sin cambios, v41) ---
        private static ObjectId SelectPolyline(Editor ed, string message)
        {
            PromptEntityOptions peo = new PromptEntityOptions(message);
            peo.SetRejectMessage("\nEl objeto seleccionado no es una Polilínea 2D.");
            peo.AddAllowedClass(typeof(Polyline), true); 
            peo.AddAllowedClass(typeof(Polyline2d), true); 
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status == PromptStatus.OK) { return per.ObjectId; }
            return ObjectId.Null;
        }

        // --- Función Auxiliar 2 (Sin cambios, v41) ---
        private static ObjectIdCollection SelectMultiplePolylines(Editor ed, string message)
        {
            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = message;
            pso.MessageForRemoval = "\nEliminar objetos de la selección:";
            TypedValue[] filter = new TypedValue[]
            {
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "POLYLINE"),
                new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                new TypedValue((int)DxfCode.Start, "POLYLINE2D"),
                new TypedValue((int)DxfCode.Operator, "OR>")
            };
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


        // --- FUNCIÓN 'RunOptimizationLoop_v37' (Sin cambios) ---
        private static LayoutResult RunOptimizationLoop_v37(Database db, ObjectId netAreaId, ObjectIdCollection affectionIds, TrackerModel tracker, double pitchEO)
        {
            LayoutResult bestLayout = new LayoutResult { TotalTrackers = 0 };

            // 1. Abrir las Polilíneas de área neta y afecciones
            Polyline netAreaPoly = null;
            List<Polyline> affectionPolys = new List<Polyline>();
            Extents3d totalExtents = new Extents3d();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                netAreaPoly = tr.GetObject(netAreaId, OpenMode.ForRead) as Polyline;
                if (netAreaPoly == null) return null; 
                totalExtents = netAreaPoly.GeometricExtents;
                
                foreach (ObjectId id in affectionIds)
                {
                    Polyline poly = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                    if (poly != null) affectionPolys.Add(poly);
                }
                tr.Abort(); 
            } 

            // 2. El Bucle de 100 Iteraciones (Optimiza N-S)
            for (int i = 0; i < 100; i++)
            {
                double currentOffsetNS = 0.1 + (i * 0.1);
                LayoutResult currentLayout = new LayoutResult 
                { 
                    OffsetEO = currentOffsetNS, 
                    TrackersToDraw = new List<Polyline>() 
                };

                // 3. Iterar la Grilla (N-S)
                // Bucle E-O (X) - Filas
                for (double x = totalExtents.MinPoint.X; x < totalExtents.MaxPoint.X; x += pitchEO)
                {
                    // Bucle N-S (Y) - Trackers
                    double y = totalExtents.MinPoint.Y;
                    while (y < totalExtents.MaxPoint.Y)
                    {
                        // 4. Probar Tracker Largo
                        Point3d centerPt = new Point3d(x + (tracker.ancho_huella_ns / 2.0), y + (tracker.longitud_largo / 2.0), 0);
                        
                        // 5. Test de Colisión (4-ESQUINAS)
                        if (IsTrackerValid_4Corners(netAreaPoly, affectionPolys, centerPt, tracker.longitud_largo, tracker.ancho_huella_ns))
                        {
                            currentLayout.LongTrackers++;
                            currentLayout.TrackersToDraw.Add(CreateTrackerPolyline_NS(centerPt, tracker.longitud_largo, tracker.ancho_huella_ns, "TRACKERS_LARGOS"));
                            y += tracker.longitud_largo + currentOffsetNS; 
                        }
                        else
                        {
                            // 6. Si el largo no cabe, probar corto
                            if (tracker.longitud_corto > 0.01)
                            {
                                centerPt = new Point3d(x + (tracker.ancho_huella_ns / 2.0), y + (tracker.longitud_corto / 2.0), 0);
                                if (IsTrackerValid_4Corners(netAreaPoly, affectionPolys, centerPt, tracker.longitud_corto, tracker.ancho_huella_ns))
                                {
                                    currentLayout.ShortTrackers++;
                                    currentLayout.TrackersToDraw.Add(CreateTrackerPolyline_NS(centerPt, tracker.longitud_corto, tracker.ancho_huella_ns, "TRACKERS_CORTOS"));
                                    y += tracker.longitud_corto + currentOffsetNS;
                                }
                                else { y += 1.0; }
                            }
                            else { y += 1.0; } 
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

        // --- 'IsPointValid' (Sin cambios) ---
        private static bool IsPointValid(Polyline netArea, List<Polyline> affections, Point3d testPoint)
        {
            // Condición 1: Debe estar DENTRO del área neta
            if (!IsPointInsidePoly(netArea, testPoint)) { return false; }
            
            // Condición 2: NO debe estar dentro de NINGUNA afección
            foreach (Polyline affPoly in affections)
            {
                if (IsPointInsidePoly(affPoly, testPoint)) { return false; }
            }
            
            return true; // Pasó ambas pruebas
        }
        
        // --- 'IsPointInsidePoly' (v43 - CORREGIDA LA PROYECCIÓN 2D PURA) ---
        private static bool IsPointInsidePoly(Polyline poly, Point3d testPointWCS)
        {
            // Esta versión ignora Z y compara X/Y (WCS) con X/Y (WCS)
            // Es una proyección "top-down" pura.

            try
            {
                int crossings = 0;
                
                // Usamos las coordenadas 2D del punto de prueba (ignorando su Z)
                double testX = testPointWCS.X;
                double testY = testPointWCS.Y;

                for (int i = 0; i < poly.NumberOfVertices; i++)
                {
                    // Obtenemos los vértices 3D (WCS) de la polilínea
                    Point3d p1_3d = poly.GetPoint3dAt(i);
                    Point3d p2_3d = poly.GetPoint3dAt((i + 1) % poly.NumberOfVertices); 

                    // Usamos solo sus coordenadas X e Y
                    double p1_X = p1_3d.X;
                    double p1_Y = p1_3d.Y;
                    double p2_X = p2_3d.X;
                    double p2_Y = p2_3d.Y;

                    if (((p1_Y <= testY && p2_Y > testY) || (p1_Y > testY && p2_Y <= testY)))
                    {
                        if (p2_Y - p1_Y == 0) continue; 
                        
                        double x_intercept = (p2_X - p1_X) * (testY - p1_Y) / (p2_Y - p1_Y) + p1_X;
                        if (testX < x_intercept)
                        {
                            crossings++;
                        }
                    }
                }
                return (crossings % 2 == 1); // Impar = Dentro
            }
            catch (System.Exception ex)
            {
                // Escribir el error en la línea de comandos para depuración
                Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\nError en IsPointInsidePoly: {ex.Message}");
                return false; 
            }
        }

        // --- 'IsTrackerValid_4Corners' (Sin cambios) ---
        private static bool IsTrackerValid_4Corners(Polyline netArea, List<Polyline> affections, Point3d center, double length, double width)
        {
            double halfLen = length / 2.0; // Largo (Y)
            double halfWid = width / 2.0;  // Ancho (X)

            Point3d p1 = new Point3d(center.X - halfWid, center.Y - halfLen, 0); // Abajo-Izquierda
            Point3d p2 = new Point3d(center.X + halfWid, center.Y - halfLen, 0); // Abajo-Derecha
            Point3d p3 = new Point3d(center.X + halfWid, center.Y + halfLen, 0); // Arriba-Derecha
            Point3d p4 = new Point3d(center.X - halfWid, center.Y + halfLen, 0); // Arriba-Izquierda

            if (!IsPointValid(netArea, affections, p1)) return false;
            if (!IsPointValid(netArea, affections, p2)) return false;
            if (!IsPointValid(netArea, affections, p3)) return false;
            if (!IsPointValid(netArea, affections, p4)) return false;

            return true; // Todas las esquinas están bien
        }


        // --- 'CreateTrackerPolyline_NS' (Sin cambios) ---
        private static Polyline CreateTrackerPolyline_NS(Point3d center, double length, double width, string layer)
        {
            double halfLen = length / 2.0; // Y-axis
            double halfWid = width / 2.0;  // X-axis
            
            Polyline rect = new Polyline();
            rect.SetDatabaseDefaults();
            rect.Layer = layer;
            
            rect.AddVertexAt(0, new Point2d(center.X - halfWid, center.Y - halfLen), 0, 0, 0); 
            rect.AddVertexAt(1, new Point2d(center.X + halfWid, center.Y - halfLen), 0, 0, 0); 
            rect.AddVertexAt(2, new Point2d(center.X + halfWid, center.Y + halfLen), 0, 0, 0); 
            rect.AddVertexAt(3, new Point2d(center.X - halfWid, center.Y + halfLen), 0, 0, 0); 
            rect.Closed = true;

            return rect;
        }
        
        // --- 'DrawFinalLayout' (v43 - CORREGIDA) ---
        private static void DrawFinalLayout(Database db, LayoutResult winningLayout)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                
                // --- CORRECCIÓN v43: El typo estaba aquí ---
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                
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
