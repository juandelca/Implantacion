using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using Autodesk.AutoCAD.Geometry; // <--- NUEVO para puntos y geometría

namespace Civil3D_Phase1
{
    public class TrackerModel
    {
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

            ed.WriteMessage("\n--- Iniciando FASE 1 (v24 - Selección de Geometría) ---");

            // --- PASO 1: Cargar Biblioteca de Trackers ---
            List<TrackerModel> trackerLibrary;
            string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string dllDirectory = Path.GetDirectoryName(dllPath);
            string jsonPath = Path.Combine(dllDirectory, "trackers.json");

            try
            {
                string jsonContent = File.ReadAllText(jsonPath);
                trackerLibrary = JsonConvert.DeserializeObject<List<TrackerModel>>(jsonContent);
                if (trackerLibrary == null || trackerLibrary.Count == 0)
                {
                    ed.WriteMessage($"\nERROR: No se pudieron cargar modelos desde 'trackers.json' o el archivo está vacío. Asegúrate de que el archivo existe en: {dllDirectory}");
                    return;
                }
                ed.WriteMessage($"\nBiblioteca 'trackers.json' cargada. {trackerLibrary.Count} modelos encontrados.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nERROR al leer 'trackers.json': {ex.Message}");
                ed.WriteMessage($"\nAsegúrate de que 'trackers.json' está en la misma carpeta que la DLL: {dllDirectory}");
                return;
            }

            // --- PASO 2: Solicitar Inputs de Layout ---
            
            // 2a. Seleccionar Tracker
            PromptKeywordOptions pkoTracker = new PromptKeywordOptions("\nSeleccione el id_tracker de la biblioteca:");
            foreach (var tracker in trackerLibrary)
            {
                pkoTracker.Keywords.Add(tracker.id_tracker);
            }
            pkoTracker.Keywords.Default = trackerLibrary[0].id_tracker;

            PromptResult prTracker = ed.GetKeywords(pkoTracker);
            if (prTracker.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n*Cancelado por el usuario.*");
                return;
            }
            string selectedTrackerId = prTracker.StringResult;
            TrackerModel selectedTracker = trackerLibrary.Find(t => t.id_tracker == selectedTrackerId);
            ed.WriteMessage($"\nTracker '{selectedTracker.id_tracker}' seleccionado. (Ancho N-S: {selectedTracker.ancho_huella_ns}m)");

            // 2b. Pedir Paso Libre N-S
            PromptDoubleOptions pdoPaso = new PromptDoubleOptions("\nIntroduzca el paso libre N-S (distancia fin-a-inicio) en metros:");
            pdoPaso.AllowNegative = false;
            pdoPaso.AllowZero = false;
            pdoPaso.DefaultValue = 3.0;

            PromptDoubleResult prPaso = ed.GetDouble(pdoPaso);
            if (prPaso.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n*Cancelado por el usuario.*");
                return;
            }
            double pasoLibreNS = prPaso.Value;
            double pitchEjeAEje = selectedTracker.ancho_huella_ns + pasoLibreNS;
            ed.WriteMessage($"\nPaso libre N-S: {pasoLibreNS}m. Pitch N-S Eje-a-Eje calculado: {pitchEjeAEje}m");

            // --- PASO 3: Selección de Geometría ---
            
            // 3a. Seleccionar Parcela
            ObjectId parcelId = SelectPolyline(ed, "\nSeleccione la Polilínea de la Parcela:");
            if (parcelId == ObjectId.Null)
            {
                ed.WriteMessage("\n*No se seleccionó una polilínea válida para la parcela. Cancelando.*");
                return;
            }
            ed.WriteMessage("\nParcela seleccionada.");

            // 3b. Seleccionar Afecciones
            ObjectIdCollection affectionIds = SelectMultiplePolylines(ed, "\nSeleccione las Polilíneas de Afecciones (o pulse Intro para ninguna):");
            ed.WriteMessage($"\n{affectionIds.Count} afecciones seleccionadas.");


            ed.WriteMessage("\n--- Todos los inputs han sido seleccionados. ---");


            // --- PASO 4: Bucle de Optimización (Lógica pendiente) ---
            
            ed.WriteMessage("\n(Bucle de Optimización pendiente...)");

            // --- PASO 5: Dibujado (Lógica pendiente) ---
            
            ed.WriteMessage($"\n(Lógica de dibujado de RECTÁNGULOS pendiente... Usará ancho {selectedTracker.ancho_huella_ns}m)");


            ed.WriteMessage("\n--- PROCESO FASE 1 TERMINADO ---");
        }

        // --- NUEVA FUNCIÓN AUXILIAR 1 ---
        /// <summary>
        /// Pide al usuario que seleccione una única Polilínea.
        /// </summary>
        private static ObjectId SelectPolyline(Editor ed, string message)
        {
            PromptEntityOptions peo = new PromptEntityOptions(message);
            peo.SetRejectMessage("\nEl objeto seleccionado no es una Polilínea.");
            peo.AddAllowedClass(typeof(Polyline), true); // Acepta Polyline 2D
            peo.AddAllowedClass(typeof(Polyline2d), true); // Acepta Polyline 2D
            peo.AddAllowedClass(typeof(Polyline3d), true); // Acepta Polyline 3D

            PromptEntityResult per = ed.GetEntity(peo);

            if (per.Status == PromptStatus.OK)
            {
                return per.ObjectId;
            }
            return ObjectId.Null;
        }

        // --- NUEVA FUNCIÓN AUXILIAR 2 ---
        /// <summary>
        /// Pide al usuario que seleccione múltiples Polilíneas.
        /// </summary>
        private static ObjectIdCollection SelectMultiplePolylines(Editor ed, string message)
        {
            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = message;
            pso.MessageForRemoval = "\nEliminar objetos de la selección:";

            // Crear un filtro para aceptar solo Polilíneas
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

            if (psr.Status == PromptStatus.OK)
            {
                return new ObjectIdCollection(psr.Value.GetObjectIds());
            }
            
            // Devuelve una colección vacía si el usuario pulsa Intro (PromptStatus.Cancel)
            return new ObjectIdCollection(); 
        }
    }
}
