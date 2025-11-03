using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using System.IO; // <--- Nuevo para leer archivos
using System.Collections.Generic; // <--- Nuevo para listas
using Newtonsoft.Json; // <--- ¡NUEVA DEPENDENCIA EXTERNA!

// Espacio de nombres de nuestro plugin
namespace Civil3D_Phase1
{
    // --- NUEVA CLASE PARA EL JSON ---
    // Esta clase debe coincidir con la estructura del trackers.json
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

    // --- CLASE PRINCIPAL DE COMANDOS ---
    public class Phase1Commands
    {
        [CommandMethod("FASE1")]
        public static void RunPhase1()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            ed.WriteMessage("\n--- Iniciando FASE 1 (v23 - Trackers Completos) ---");

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

            // --- PASO 2: Solicitar Inputs al Usuario ---

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
            pdoPaso.DefaultValue = 3.0; // Un valor por defecto razonable

            PromptDoubleResult prPaso = ed.GetDouble(pdoPaso);
            if (prPaso.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n*Cancelado por el usuario.*");
                return;
            }
            double pasoLibreNS = prPaso.Value;
            double pitchEjeAEje = selectedTracker.ancho_huella_ns + pasoLibreNS;
            ed.WriteMessage($"\nPaso libre N-S: {pasoLibreNS}m. Pitch N-S Eje-a-Eje calculado: {pitchEjeAEje}m");

            // --- PASO 3: Selección de Geometría (Lógica que ya teníamos) ---
            
            // (Aquí iría la lógica de `SelectParcelPolyline` y `SelectAffectionPolylines` que implementaremos)
            
            ed.WriteMessage("\n(Lógica de selección de polilíneas pendiente...)");


            // --- PASO 4: Bucle de Optimización (Lógica pendiente) ---
            
            ed.WriteMessage("\n(Bucle de Optimización pendiente...)");

            // --- PASO 5: Dibujado (Lógica pendiente, ahora usará 'selectedTracker') ---
            // Aquí es donde usaríamos 'selectedTracker.longitud_largo', 'selectedTracker.longitud_corto'
            // y 'selectedTracker.ancho_huella_ns' para dibujar RECTÁNGULOS.
            
            ed.WriteMessage($"\n(Lógica de dibujado de RECTÁNGULOS pendiente... Usará ancho {selectedTracker.ancho_huella_ns}m)");


            ed.WriteMessage("\n--- PROCESO FASE 1 TERMINADO (Lógica principal conectada) ---");
        }
    }
}
