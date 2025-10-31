/* --- Dependencias de .NET --- */
using System;
using System.Collections.Generic;

/* --- Dependencias de AutoCAD --- */
// Estas son las APIs base de AutoCAD
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

/* --- Dependencias de Civil 3D --- */
// Estas son las APIs específicas de Civil 3D (para Terrenos, etc.)
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

// Definimos un alias para nuestro proyecto para que sea fácil de llamar
[assembly: CommandClass(typeof(Civil3D_Phase1.Phase1Commands))]

namespace Civil3D_Phase1
{
    // -----------------------------------------------------------------
    // CLASE DE INICIALIZACIÓN (IEntryPoint)
    // -----------------------------------------------------------------
    // Esta clase se ejecuta cuando Civil 3D carga la DLL.
    // La usaremos para imprimir un mensaje en la línea de comandos
    // y saber que nuestro plugin se ha cargado correctamente.
    // -----------------------------------------------------------------
    public class PluginInitializer : IExtensionApplication
    {
        public void Initialize()
        {
            // Obtenemos el editor de AutoCAD para poder escribir en la línea de comandos
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;
            ed.WriteMessage("\n--- Plugin Fase 1 (Layout 2D) cargado con éxito. ---");
            ed.WriteMessage("\n--- Escriba 'FASE1' para ejecutar el análisis. ---");
        }

        public void Terminate()
        {
            // Este método se llama cuando se descarga el plugin o se cierra Civil 3D
        }
    }

    // -----------------------------------------------------------------
    // CLASE DE COMANDOS
    // -----------------------------------------------------------------
    // Aquí es donde vivirá la lógica principal de la Fase 1.
    // Crearemos un comando llamado "FASE1"
    // -----------------------------------------------------------------
    public class Phase1Commands
    {
        // El atributo [CommandMethod("FASE1")] convierte este método
        // en un comando que el usuario puede teclear en Civil 3D.
        [CommandMethod("FASE1")]
        public void RunPhase1()
        {
            // --- 0. OBTENER DOCUMENTOS Y EDITOR ---
            // Necesitamos acceder al documento activo, la base de datos y el editor (línea de comandos)
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            CivilDocument cdoc = CivilApplication.ActiveDocument;

            ed.WriteMessage("\nIniciando Fase 1: Optimización de Layout 2D...");

            // --- 1. SELECCIÓN DE OBJETOS (INPUTS) ---
            // Aquí le pediremos al usuario que seleccione los objetos
            // (Parcela, Afecciones y Terreno)

            // TODO: Pedir al usuario que seleccione la Polilínea de Parcela
            // TODO: Pedir al usuario que seleccione las Polilíneas de Afecciones
            // TODO: Pedir al usuario que seleccione la Superficie TIN (Terreno_Original)

            ed.WriteMessage("\nInputs seleccionados. (Lógica de selección pendiente)");


            // --- 2. LÓGICA DEL "PLANO" (PASO 1 al 5) ---

            // TODO: PASO 1: Crear el "Mapa de Validez"
            //   - 1a. Análisis 2D (Parcela - Afecciones) = Area_Neta
            //   - 1b. Análisis 3D (Pendiente N-S <= 15%)
            //   - 1c. Combinar (Area_Neta INTERSECCIÓN Zonas_Pendiente_OK) = Mapa_Valido

            // TODO: PASO 2: Bucle de Optimización (Offset E-O)
            //   - Bucle de 0.0m a 9.9m (incremento 10cm)
            
            // TODO: PASO 3: Iteración del Bucle
            //   - 3a. Colocar rejilla N-S con el offset actual
            //   - 3b. Recortar rejilla contra el "Mapa Válido"
            //   - 3c. Llenar ejes (Prioridad 37.7m, luego 17.4m)
            //   - 3d. Guardar resultado (Offset, Total Strings, Largos, Cortos)

            // TODO: PASO 4: El Ganador
            //   - 4a. Buscar el layout que sume 400 strings.
            //   - 4b. Si hay varios, el que tenga más trackers largos.
            //   - 4c. Si no, el más cercano por debajo (ej. 399).

            // TODO: PASO 5: Resultado (Output)
            //   - 5a. Dibujar las polilíneas del "Layout Ganador" en el plano.


            ed.WriteMessage("\n--- PROCESO FASE 1 TERMINADO (Lógica pendiente) ---");
        }
    }
}
