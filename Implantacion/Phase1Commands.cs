using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors;

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

            ed.WriteMessage("\n--- Iniciando FASE 1 (v26 - Cálculo Área Neta) ---");

            // --- PASO 1: Cargar Biblioteca de Trackers ---
            List<TrackerModel> trackerLibrary;
            string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string dllDirectory = Path.GetDirectoryName(dllPath);
            string jsonPath = Path.Combine(dllDirectory, "trackers.json");

            try
            {
                // ... (Lógica de carga de JSON sin cambios) ...
                string jsonContent = File.ReadAllText(jsonPath);
                trackerLibrary = JsonConvert.DeserializeObject<List<TrackerModel>>(jsonContent);
                if (trackerLibrary == null || trackerLibrary.Count == 0) { /* ... error ... */ return; }
                ed.WriteMessage($"\nBiblioteca 'trackers.json' cargada. {trackerLibrary.Count} modelos encontrados.");
            }
            catch (System.Exception ex) { /* ... error ... */ return; }


            // --- PASO 2: Solicitar Inputs de Layout ---
            
            // 2a. Seleccionar Tracker
            // ... (Lógica de selección de tracker sin cambios) ...
            PromptKeywordOptions pkoTracker = new PromptKeywordOptions("\nSeleccione el id_tracker de la biblioteca:");
            foreach (var tracker in trackerLibrary) { pkoTracker.Keywords.Add(tracker.id_tracker); }
            pkoTracker.Keywords.Default = trackerLibrary[0].id_tracker;
            PromptResult prTracker = ed.GetKeywords(pkoTracker);
            if (prTracker.Status != PromptStatus.OK) { return; }
            string selectedTrackerId = prTracker.StringResult;
            TrackerModel selectedTracker = trackerLibrary.Find(t => t.id_tracker == selectedTrackerId);
            ed.WriteMessage($"\nTracker '{selectedTracker.id_tracker}' seleccionado. (Ancho N-S: {selectedTracker.ancho_huella_ns}m)");

            // 2b. Pedir Paso Libre N-S
            // ... (Lógica de paso N-S sin cambios) ...
            PromptDoubleOptions pdoPaso = new PromptDoubleOptions("\nIntroduzca el paso libre N-S (distancia fin-a-inicio) en metros:");
            pdoPaso.AllowNegative = false; pdoPaso.AllowZero = false; pdoPaso.DefaultValue = 3.0;
            PromptDoubleResult prPaso = ed.GetDouble(pdoPaso);
            if (prPaso.Status != PromptStatus.OK) { return; }
            double pasoLibreNS = prPaso.Value;
            double pitchEjeAEje = selectedTracker.ancho_huella_ns + pasoLibreNS;
            ed.WriteMessage($"\nPaso libre N-S: {pasoLibreNS}m. Pitch N-S Eje-a-Eje calculado: {pitchEjeAEje}m");

            // --- NUEVO Input 2c. Pedir Retranqueo (Setback) ---
            PromptDoubleOptions pdoSetback = new PromptDoubleOptions("\nIntroduzca el retranqueo (setback) de la parcela en metros:");
            pdoSetback.AllowNegative = false;
            pdoSetback.AllowZero = true;
            pdoSetback.DefaultValue = 5.0; // Setback de 5m por defecto

            PromptDoubleResult prSetback = ed.GetDouble(pdoSetback);
            if (prSetback.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n*Cancelado por el usuario.*");
                return;
            }
            double setback = prSetback.Value;
            ed.WriteMessage($"\nRetranqueo seleccionado: {setback}m");


            // --- PASO 3: Selección de Geometría ---
            
            // 3a. Seleccionar Parcela
            // ... (Lógica de selección de parcela sin cambios) ...
            ObjectId parcelId = SelectPolyline(ed, "\nSeleccione la Polilínea de la Parcela:");
            if (parcelId == ObjectId.Null) { return; }
            ed.WriteMessage("\nParcela seleccionada.");

            // 3b. Seleccionar Afecciones
            // ... (Lógica de selección de afecciones sin cambios) ...
            ObjectIdCollection affectionIds = SelectMultiplePolylines(ed, "\nSeleccione las Polilíneas de Afecciones (o pulse Intro para ninguna):");
            ed.WriteMessage($"\n{affectionIds.Count} afecciones seleccionadas.");

            ed.WriteMessage("\n--- Todos los inputs han sido seleccionados. ---");


            // --- PASO 4: Cálculo de Área Neta ---
            ed.WriteMessage("\nIniciando Paso 4: Cálculo del Área Neta...");
            
            // --- LÓGICA REEMPLAZADA ---
            // Llama a la nueva función para calcular el offset (retranqueo)
            ObjectId netAreaId = GetNetArea(db, parcelId, setback);
            
            if (netAreaId == ObjectId.Null)
            {
                ed.WriteMessage("\nERROR: No se pudo calcular el Área Neta (el retranqueo puede ser demasiado grande o la polilínea no es válida). Cancelando.");
                return;
            }
            
            // (En el futuro, aquí también restaremos las 'affectionIds' de 'netAreaId')
            
            ed.WriteMessage("\n¡Área Neta (retranqueo) calculada y dibujada con éxito en la capa 'AREA_NETA'!");


            // --- PASO 5: Bucle de Optimización (Lógica pendiente) ---
            ed.WriteMessage("\n(Bucle de Optimización pendiente...)");


            // --- PASO 6: Dibujado (Implementación de prueba) ---
            DrawTestTrackers(db, selectedTracker); // Mantenemos esto por ahora
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

        // --- NUEVA FUNCIÓN AUXILIAR 5 ---
        /// <summary>
        /// Aplica un offset (retranqueo) a la polilínea de la parcela.
        /// Dibuja la polilínea resultante en la capa "AREA_NETA".
        /// </summary>
        /// <returns>El ObjectId de la nueva polilínea (Área Neta) o ObjectId.Null si falla.</returns>
        private static ObjectId GetNetArea(Database db, ObjectId parcelId, double setback)
        {
            // El setback 0 significa que no hay offset, el área neta es la parcela misma.
            if (setback == 0)
            {
                return parcelId;
            }

            // Usamos una transacción para leer la polilínea y crear la nueva
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // 1. Abrir la polilínea de la parcela
                Entity ent = tr.GetObject(parcelId, OpenMode.ForRead) as Entity;
                if (!(ent is Polyline) && !(ent is Polyline2d) && !(ent is Polyline3d))
                {
                    return ObjectId.Null; // No es una polilínea
                }

                // 2. Crear la capa de Área Neta
                string layerName = "AREA_NETA";
                Color color = Color.FromRgb(255, 0, 255); // Magenta
                CreateLayer(db, tr, layerName, color);

                // 3. Aplicar el Offset
                // Usamos un valor negativo para hacer el offset hacia *adentro*
                DBObjectCollection offsetCurves = null;
                try
                {
                    // Debemos usar la interfaz 'Curve' para acceder a GetOffsetCurves
                    Curve curve = ent as Curve; 
                    if (curve == null) return ObjectId.Null;

                    // El método GetOffsetCurves devuelve una colección de DBObject
                    offsetCurves = curve.GetOffsetCurves(-setback);
                }
                catch (System.Exception ex)
                {
                    Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\nError durante el Offset: {ex.Message}");
                    return ObjectId.Null; // Falla si el setback es muy grande, p.ej.
                }


                // 4. Procesar el resultado del Offset
                if (offsetCurves != null && offsetCurves.Count > 0)
                {
                    // El offset puede generar múltiples polilíneas, pero para una parcela simple, será 1.
                    // Nos quedamos con la primera entidad.
                    Entity netAreaEntity = offsetCurves[0] as Entity; 
                    
                    if (netAreaEntity != null)
                    {
                        // 5. Añadir la nueva entidad al ModelSpace y a la transacción
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                        netAreaEntity.Layer = layerName; // Asignar la capa magenta
                        btr.AppendEntity(netAreaEntity);
                        tr.AddNewlyCreatedDBObject(netAreaEntity, true);

                        // 6. Confirmar la transacción
                        tr.Commit();
                        
                        // Devolver el ID de la nueva polilínea creada
                        return netAreaEntity.ObjectId;
                    }
                }
                
                // Si el offset no devolvió nada (p.ej. setback demasiado grande)
                return ObjectId.Null;
            }
        }
    }
}
