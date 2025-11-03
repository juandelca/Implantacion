using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors;
using System.Linq; // <--- NUEVO para listas

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

    public class Phase1Commands
    {
        [CommandMethod("FASE1")]
        public static void RunPhase1()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            ed.WriteMessage("\n--- Iniciando FASE 1 (v28 - Resta de Afecciones) ---");

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
            if (netAreaId == ObjectId.Null)
            {
                ed.WriteMessage("\nERROR: No se pudo calcular el Área Neta (retranqueo). Cancelando.");
                return;
            }
            ed.WriteMessage("\n¡Área Neta (retranqueo) calculada y dibujada en 'AREA_NETA'!");

            // 4b. Restar Afecciones
            ObjectIdCollection finalValidAreaIds = new ObjectIdCollection();
            if (affectionIds.Count > 0)
            {
                ed.WriteMessage("\nRestando afecciones del Área Neta...");
                finalValidAreaIds = SubtractAffections(db, netAreaId, affectionIds);
                if (finalValidAreaIds.Count == 0)
                {
                    ed.WriteMessage("\nERROR: La resta de afecciones falló o resultó en un área vacía.");
                    return;
                }
                ed.WriteMessage($"\n¡Afecciones restadas con éxito! Resultado dibujado en 'AREA_VALIDA_FINAL'.");
            }
            else
            {
                // Si no hay afecciones, el área neta ES el área final.
                finalValidAreaIds.Add(netAreaId);
            }
            
            ed.WriteMessage("\n¡Mapa de Validez (Solo 2D) calculado con éxito!");


            // --- PASO 5: Bucle de Optimización (Lógica pendiente) ---
            // (El siguiente paso será usar 'finalValidAreaIds' para la optimización)
            ed.WriteMessage("\n(Bucle de Optimización pendiente...)");


            // --- PASO 6: Dibujado (Implementación de prueba) ---
            DrawTestTrackers(db, selectedTracker);
            ed.WriteMessage("\n¡Trackers de prueba dibujados con éxito!");

            ed.WriteMessage("\n--- PROCESO FASE 1 TERMINADO ---");
        }

        // --- Función Auxiliar 1 (Sin cambios) ---
        private static ObjectId SelectPolyline(Editor ed, string message)
        {
            // ... (Esta función no cambia) ...
            PromptEntityOptions peo = new PromptEntityOptions(message);
            peo.SetRejectMessage("\nEl objeto seleccionado no es una Polilínea.");
            peo.AddAllowedClass(typeof(Polyline), true);
            peo.AddAllowedClass(typeof(Polyline2d), true);
            peo.AddAllowedClass(typeof(Polyline3d), true);
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status == PromptStatus.OK) { return per.ObjectId; }
            return ObjectId.Null;
        }

        // --- Función Auxiliar 2 (Sin cambios) ---
        private static ObjectIdCollection SelectMultiplePolylines(Editor ed, string message)
        {
            // ... (Esta función no cambia) ...
            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = message; pso.MessageForRemoval = "\nEliminar objetos de la selección:";
            TypedValue[] filter = new TypedValue[] { new TypedValue((int)DxfCode.Operator, "<OR"), new TypedValue((int)DxfCode.Start, "POLYLINE"), new TypedValue((int)DxfCode.Start, "LWPOLYLINE"), new TypedValue((int)DxfCode.Start, "POLYLINE2D"), new TypedValue((int)DxfCode.Start, "POLYLINE3D"), new TypedValue((int)DxfCode.Operator, "OR>") };
            SelectionFilter selFilter = new SelectionFilter(filter);
            PromptSelectionResult psr = ed.GetSelection(pso, selFilter);
            if (psr.Status == PromptStatus.OK) { return new ObjectIdCollection(psr.Value.GetObjectIds()); }
            return new ObjectIdCollection(); 
        }

        // --- Función Auxiliar 3 (Sin cambios) ---
        private static void DrawTestTrackers(Database db, TrackerModel tracker)
        {
            // ... (Esta función no cambia) ...
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                string layerLargo = "TRACKERS_LARGOS"; string layerCorto = "TRACKERS_CORTOS";
                Color colorLargo = Color.FromRgb(0, 100, 255); Color colorCorto = Color.FromRgb(255, 100, 0);
                CreateLayer(db, tr, layerLargo, colorLargo);
                CreateLayer(db, tr, layerCorto, colorCorto);
                Polyline rectLargo = new Polyline(); rectLargo.SetDatabaseDefaults(); rectLargo.Layer = layerLargo;
                rectLargo.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                rectLargo.AddVertexAt(1, new Point2d(tracker.longitud_largo, 0), 0, 0, 0);
                rectLargo.AddVertexAt(2, new Point2d(tracker.longitud_largo, tracker.ancho_huella_ns), 0, 0, 0);
                rectLargo.AddVertexAt(3, new Point2d(0, tracker.ancho_huella_ns), 0, 0, 0);
                rectLargo.Closed = true;
                double offsetX = tracker.longitud_largo + 5.0;
                Polyline rectCorto = new Polyline(); rectCorto.SetDatabaseDefaults(); rectCorto.Layer = layerCorto;
                rectCorto.AddVertexAt(0, new Point2d(offsetX, 0), 0, 0, 0);
                rectCorto.AddVertexAt(1, new Point2d(offsetX + tracker.longitud_corto, 0), 0, 0, 0);
                rectCorto.AddVertexAt(2, new Point2d(offsetX + tracker.longitud_corto, tracker.ancho_huella_ns), 0, 0, 0);
                rectCorto.AddVertexAt(3, new Point2d(offsetX, tracker.ancho_huella_ns), 0, 0, 0);
                rectCorto.Closed = true;
                btr.AppendEntity(rectLargo); btr.AppendEntity(rectCorto);
                tr.AddNewlyCreatedDBObject(rectLargo, true); tr.AddNewlyCreatedDBObject(rectCorto, true);
                tr.Commit();
            }
        }

        // --- Función Auxiliar 4 (Sin cambios) ---
        private static void CreateLayer(Database db, Transaction tr, string layerName, Color color)
        {
            // ... (Esta función no cambia) ...
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

        // --- Función Auxiliar 5 ('GetNetArea', sin cambios) ---
        private static ObjectId GetNetArea(Database db, ObjectId parcelId, double setback)
        {
            // ... (Esta función no cambia) ...
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
                try { offsetCurves = curve.GetOffsetCurves(-setback); }
                catch (System.Exception) { return ObjectId.Null; }
                if (offsetCurves != null && offsetCurves.Count > 0)
                {
                    Curve offsetCurve = offsetCurves[0] as Curve;
                    if (offsetCurve != null && offsetCurve.Area < originalArea)
                    {
                        return AddNetAreaToModelSpace(db, tr, offsetCurve, layerName);
                    }
                }
                try { offsetCurves = curve.GetOffsetCurves(setback); }
                catch (System.Exception) { return ObjectId.Null; }
                if (offsetCurves != null && offsetCurves.Count > 0)
                {
                     Curve offsetCurve = offsetCurves[0] as Curve;
                     if (offsetCurve != null && offsetCurve.Area < originalArea)
                     {
                        return AddNetAreaToModelSpace(db, tr, offsetCurve, layerName);
                     }
                }
                return ObjectId.Null;
            }
        }

        // --- Función Auxiliar 6 (Sin cambios) ---
        private static ObjectId AddNetAreaToModelSpace(Database db, Transaction tr, Curve netAreaCurve, string layerName)
        {
            // ... (Esta función no cambia) ...
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            Entity netAreaEntity = netAreaCurve as Entity;
            netAreaEntity.Layer = layerName;
            btr.AppendEntity(netAreaEntity);
            tr.AddNewlyCreatedDBObject(netAreaEntity, true);
            tr.Commit();
            return netAreaEntity.ObjectId;
        }

        // --- NUEVA FUNCIÓN AUXILIAR 7 ---
        /// <summary>
        /// Resta un conjunto de afecciones de una polilínea de área neta.
        /// Dibuja el resultado en la capa 'AREA_VALIDA_FINAL'.
        /// </summary>
        /// <returns>Una colección de ObjectIds de las polilíneas resultantes.</returns>
        private static ObjectIdCollection SubtractAffections(Database db, ObjectId netAreaId, ObjectIdCollection affectionIds)
        {
            ObjectIdCollection finalAreaIds = new ObjectIdCollection();
            string layerName = "AREA_VALIDA_FINAL";
            Color color = Color.FromRgb(0, 255, 0); // Verde

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    // 1. Crear la nueva capa
                    CreateLayer(db, tr, layerName, color);

                    // 2. Abrir el área neta y convertirla a Región
                    Curve netAreaCurve = tr.GetObject(netAreaId, OpenMode.ForRead) as Curve;
                    if (netAreaCurve == null) return finalAreaIds;
                    
                    DBObjectCollection netAreaRegions = new DBObjectCollection();
                    // Convertir la curva a Región (Region.CreateFromCurves devuelve una colección)
                    netAreaRegions = Region.CreateFromCurves(new DBObjectCollection { netAreaCurve });
                    if (netAreaRegions.Count == 0) return finalAreaIds;
                    Region baseRegion = netAreaRegions[0] as Region;

                    // 3. Procesar y restar cada afección
                    foreach (ObjectId affId in affectionIds)
                    {
                        Curve affCurve = tr.GetObject(affId, OpenMode.ForRead) as Curve;
                        if (affCurve == null) continue;

                        DBObjectCollection affRegions = Region.CreateFromCurves(new DBObjectCollection { affCurve });
                        if (affRegions.Count > 0)
                        {
                            Region affRegion = affRegions[0] as Region;
                            // La operación booleana (resta) modifica la 'baseRegion'
                            baseRegion.BooleanOperation(BooleanOperationType.BoolSubtract, affRegion);
                        }
                    }

                    // 4. Si la región base (ahora con agujeros) sigue existiendo
                    if (!baseRegion.IsDisposed && baseRegion.Area > 0.001)
                    {
                        // 5. Añadir la región resultante al ModelSpace
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                        
                        baseRegion.Layer = layerName;
                        btr.AppendEntity(baseRegion);
                        tr.AddNewlyCreatedDBObject(baseRegion, true);
                        
                        // NOTA: 'baseRegion' es ahora el área válida.
                        // Para la optimización, podríamos querer explotarla (Explode) de nuevo a Polilíneas
                        // Pero por ahora, la dibujamos como Región.
                        finalAreaIds.Add(baseRegion.ObjectId);
                    }

                    // 6. Confirmar la transacción
                    tr.Commit();
                    return finalAreaIds;
                }
                catch (System.Exception ex)
                {
                    Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\nERROR durante la resta booleana: {ex.Message}");
                    tr.Abort();
                    return new ObjectIdCollection(); // Devolver vacío si falla
                }
            }
        }
    }
}
