using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors; // <--- NUEVO para gestionar colores de capas

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

            ed.WriteMessage("\n--- Iniciando FASE 1 (v25 - Dibujado de Rectángulos) ---");

            // --- PASO 1: Cargar Biblioteca de Trackers ---
            List<TrackerModel> trackerLibrary;
            string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string dllDirectory = Path.GetDirectoryName(dllPath);
            string jsonPath = Path.Combine(dllDirectory, "trackers.json");

            try
            {
                // ... (Esta parte no cambia) ...
                string jsonContent = File.ReadAllText(jsonPath);
                trackerLibrary = JsonConvert.DeserializeObject<List<TrackerModel>>(jsonContent);
                if (trackerLibrary == null || trackerLibrary.Count == 0) { /* ... error ... */ return; }
                ed.WriteMessage($"\nBiblioteca 'trackers.json' cargada. {trackerLibrary.Count} modelos encontrados.");
            }
            catch (System.Exception ex) { /* ... error ... */ return; }

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
            pdoPaso.AllowNegative = false;
            pdoPaso.AllowZero = false;
            pdoPaso.DefaultValue = 3.0;
            PromptDoubleResult prPaso = ed.GetDouble(pdoPaso);
            if (prPaso.Status != PromptStatus.OK) { return; }
            double pasoLibreNS = prPaso.Value;
            double pitchEjeAEje = selectedTracker.ancho_huella_ns + pasoLibreNS;
            ed.WriteMessage($"\nPaso libre N-S: {pasoLibreNS}m. Pitch N-S Eje-a-Eje calculado: {pitchEjeAEje}m");

            // --- PASO 3: Selección de Geometría ---
            
            // 3a. Seleccionar Parcela
            ObjectId parcelId = SelectPolyline(ed, "\nSeleccione la Polilínea de la Parcela:");
            if (parcelId == ObjectId.Null) { return; }
            ed.WriteMessage("\nParcela seleccionada.");

            // 3b. Seleccionar Afecciones
            ObjectIdCollection affectionIds = SelectMultiplePolylines(ed, "\nSeleccione las Polilíneas de Afecciones (o pulse Intro para ninguna):");
            ed.WriteMessage($"\n{affectionIds.Count} afecciones seleccionadas.");

            ed.WriteMessage("\n--- Todos los inputs han sido seleccionados. ---");

            // --- PASO 4: Cálculo de Área Neta (Lógica pendiente) ---
            ed.WriteMessage("\n(Cálculo de Área Neta pendiente...)");

            // --- PASO 5: Bucle de Optimización (Lógica pendiente) ---
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
            pso.MessageForAdding = message;
            pso.MessageForRemoval = "\nEliminar objetos de la selección:";
            TypedValue[] filter = new TypedValue[]
            {
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "POLYLINE"),
                new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                new TypedValue((int)DxfCode.Start, "POLYLINE2D"),
                new TypedValue((int)DxfCode.Start, "POLYLINE3D"),
                new TypedValue((int)DxfCode.Operator, "OR>")
            };
            SelectionFilter selFilter = new SelectionFilter(filter);
            PromptSelectionResult psr = ed.GetSelection(pso, selFilter);
            if (psr.Status == PromptStatus.OK) { return new ObjectIdCollection(psr.Value.GetObjectIds()); }
            return new ObjectIdCollection(); 
        }

        // --- NUEVA FUNCIÓN AUXILIAR 3 ---
        /// <summary>
        /// Dibuja un tracker largo y uno corto en el origen (0,0) como prueba.
        /// </summary>
        private static void DrawTestTrackers(Database db, TrackerModel tracker)
        {
            // Usaremos una transacción para hacer cambios en la base de datos (dibujar)
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // 1. Abrir la tabla de Bloques (donde está el ModelSpace)
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // 2. Definir Nombres y Colores de Capas
                string layerLargo = "TRACKERS_LARGOS";
                string layerCorto = "TRACKERS_CORTOS";
                Color colorLargo = Color.FromRgb(0, 100, 255); // Azul
                Color colorCorto = Color.FromRgb(255, 100, 0); // Naranja

                // 3. Crear las capas si no existen
                CreateLayer(db, tr, layerLargo, colorLargo);
                CreateLayer(db, tr, layerCorto, colorCorto);

                // 4. Crear Geometría (Rectángulos)
                // Usamos Polilíneas para dibujar rectángulos

                // 4a. Tracker Largo (en 0,0)
                Polyline rectLargo = new Polyline();
                rectLargo.SetDatabaseDefaults();
                rectLargo.Layer = layerLargo;
                // Coordenadas del rectángulo
                rectLargo.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                rectLargo.AddVertexAt(1, new Point2d(tracker.longitud_largo, 0), 0, 0, 0);
                rectLargo.AddVertexAt(2, new Point2d(tracker.longitud_largo, tracker.ancho_huella_ns), 0, 0, 0);
                rectLargo.AddVertexAt(3, new Point2d(0, tracker.ancho_huella_ns), 0, 0, 0);
                rectLargo.Closed = true; // Cerrar el rectángulo

                // 4b. Tracker Corto (lo dibujaremos 5m a la derecha del largo)
                double offsetX = tracker.longitud_largo + 5.0;
                Polyline rectCorto = new Polyline();
                rectCorto.SetDatabaseDefaults();
                rectCorto.Layer = layerCorto;
                rectCorto.AddVertexAt(0, new Point2d(offsetX, 0), 0, 0, 0);
                rectCorto.AddVertexAt(1, new Point2d(offsetX + tracker.longitud_corto, 0), 0, 0, 0);
                rectCorto.AddVertexAt(2, new Point2d(offsetX + tracker.longitud_corto, tracker.ancho_huella_ns), 0, 0, 0);
                rectCorto.AddVertexAt(3, new Point2d(offsetX, tracker.ancho_huella_ns), 0, 0, 0);
                rectCorto.Closed = true;

                // 5. Añadir los rectángulos al ModelSpace
                btr.AppendEntity(rectLargo);
                btr.AppendEntity(rectCorto);
                
                // 6. Añadirlos a la transacción
                tr.AddNewlyCreatedDBObject(rectLargo, true);
                tr.AddNewlyCreatedDBObject(rectCorto, true);

                // 7. Confirmar todos los cambios
                tr.Commit();
            }
        }

        // --- NUEVA FUNCIÓN AUXILIAR 4 ---
        /// <summary>
        /// Crea una capa si no existe.
        /// </summary>
        private static void CreateLayer(Database db, Transaction tr, string layerName, Color color)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName))
            {
                // Si no existe, abrimos la tabla para escribir
                lt.UpgradeOpen();
                LayerTableRecord ltr = new LayerTableRecord();
                ltr.Name = layerName;
                ltr.Color = color;
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }
    }
}
