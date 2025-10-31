/* --- Dependencias de .NET --- */
using System;
using System.Collections.Generic;

/* --- Dependencias de AutoCAD --- */
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

/* --- Dependencias de Civil 3D --- */
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

// Definimos un alias para nuestro proyecto para que sea fácil de llamar
[assembly: CommandClass(typeof(Civil3D_Phase1.Phase1Commands))]

namespace Civil3D_Phase1
{
    // -----------------------------------------------------------------
    // CLASE DE INICIALIZACIÓN (IEntryPoint)
    // -----------------------------------------------------------------
    public class PluginInitializer : IExtensionApplication
    {
        public void Initialize()
        {
            if (Application.DocumentManager.MdiActiveDocument != null)
            {
                Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;
                ed.WriteMessage("\n--- Plugin Fase 1 (Layout 2D) cargado con éxito. ---");
                ed.WriteMessage("\n--- Escriba 'FASE1' para ejecutar el análisis. ---");
            }
        }

        public void Terminate()
        {
            // Este método se llama cuando se descarga el plugin o se cierra Civil 3D
        }
    }

    // -----------------------------------------------------------------
    // CLASE DE COMANDOS
    // -----------------------------------------------------------------
    public class Phase1Commands
    {
        [CommandMethod("FASE1")]
        public void RunPhase1()
        {
            // --- 0. OBTENER DOCUMENTOS Y EDITOR ---
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            CivilDocument cdoc = CivilApplication.ActiveDocument;

            ed.WriteMessage("\nIniciando Fase 1: Optimización de Layout 2D...");

            // --- 1. SELECCIÓN DE OBJETOS (INPUTS) ---
            
            // --- 1a. Seleccionar la Parcela (Polilínea) ---
            PromptEntityOptions peoParcela = new PromptEntityOptions("\nSeleccione la Polilínea de la Parcela: ");
            peoParcela.SetRejectMessage("\nEl objeto seleccionado no es una Polilínea. Inténtelo de nuevo.");
            peoParcela.AddAllowedClass(typeof(Polyline), true);
            
            PromptEntityResult perParcela = ed.GetEntity(peoParcela);
            if (perParcela.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n*Cancelado* No se seleccionó la parcela.");
                return;
            }
            ObjectId parcelaId = perParcela.ObjectId;
            ed.WriteMessage("\nParcela seleccionada.");

            // --- 1b. Seleccionar las Afecciones (Múltiples Polilíneas) ---
            PromptSelectionOptions psoAfecciones = new PromptSelectionOptions();
            psoAfecciones.MessageForAdding = "\nSeleccione las Polilíneas de Afecciones (o pulse Intro para ninguna): ";
            psoAfecciones.MessageForRemoval = "\nEliminar objetos de la selección: ";
            
            TypedValue[] tvs = new TypedValue[] {
                new TypedValue((int)DxfCode.Start, "POLYLINE,LWPOLYLINE")
            };
            SelectionFilter filter = new SelectionFilter(tvs);
            
            PromptSelectionResult psrAfecciones = ed.GetSelection(psoAfecciones, filter);
            List<ObjectId> afeccionesIds = new List<ObjectId>();

            if (psrAfecciones.Status == PromptStatus.OK)
            {
                afeccionesIds.AddRange(psrAfecciones.Value.GetObjectIds());
                ed.WriteMessage($"\n{afeccionesIds.Count} afecciones seleccionadas.");
            }
            else
            {
                ed.WriteMessage("\nNo se seleccionaron afecciones.");
            }

            // --- 1c. Seleccionar el Terreno Original (TIN Surface) ---
            PromptEntityOptions peoTerreno = new PromptEntityOptions("\nSeleccione la Superficie (Terreno Original): ");
            peoTerreno.SetRejectMessage("\nEl objeto seleccionado no es una Superficie TIN.");
            peoTerreno.AddAllowedClass(typeof(TinSurface), true);
            
            PromptEntityResult perTerreno = ed.GetEntity(peoTerreno);
            if (perTerreno.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n*Cancelado* No se seleccionó el terreno.");
                return;
            }
            ObjectId terrenoId = perTerreno.ObjectId;
            ed.WriteMessage("\nTerreno seleccionado.");

            ed.WriteMessage("\n--- Todos los inputs han sido seleccionados. ---");

            // --- 2. TRANSACCIÓN PARA PROCESAR LOS DATOS ---
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    // --- PASO 1a: ANÁLISIS 2D (PARCELA - AFECCIONES) ---
                    ed.WriteMessage("\nIniciando Paso 1a: Cálculo del Área Neta (Parcela - Afecciones)...");

                    // Convertimos la polilínea de Parcela en una Región
                    Polyline parcelaPoly = tr.GetObject(parcelaId, OpenMode.ForRead) as Polyline;
                    if (!parcelaPoly.Closed)
                    {
                        ed.WriteMessage("\nError: La polilínea de parcela no está cerrada. Abortando.");
                        tr.Abort();
                        return;
                    }

                    DBObjectCollection parcelaCurves = new DBObjectCollection();
                    parcelaCurves.Add(parcelaPoly);
                    Region parcelaRegion = (Region.CreateFromCurves(parcelaCurves)[0] as Region);
                    
                    // Restamos cada afección
                    foreach (ObjectId afeccionId in afeccionesIds)
                    {
                        DBObject afeccionObj = tr.GetObject(afeccionId, OpenMode.ForRead);
                        Polyline afeccionPoly = afeccionObj as Polyline;
                        if (afeccionPoly == null) continue; // Ignorar si no es una polilínea válida
                        
                        if (!afeccionPoly.Closed)
                        {
                            // Si no está cerrada, intentamos cerrarla para la operación
                            // Nota: Esto requiere abrirla para escritura
                            afeccionPoly.UpgradeOpen();
                            afeccionPoly.Closed = true;
                        }

                        DBObjectCollection afeccionCurves = new DBObjectCollection();
                        afeccionCurves.Add(afeccionPoly);
                        Region afeccionRegion = (Region.CreateFromCurves(afeccionCurves)[0] as Region);
                        
                        // Esta es la operación de resta booleana
                        parcelaRegion.BooleanOperation(BooleanOperationType.BoolSubtract, afeccionRegion);
                    }
                    
                    // Al final del bucle, 'parcelaRegion' contiene el 'Area_Neta'
                    ed.WriteMessage("\nÁrea Neta 2D (Región) calculada con éxito.");

                    // --- PASO 1b: ANÁLISIS 3D (PENDIENTE) ---
                    // TODO: Abrir el 'terreno' y analizar la pendiente N-S <= 15%
                    ed.WriteMessage("\n(TODO: Implementar lógica de análisis de pendiente del terreno)");


                    // --- PASO 1c: COMBINAR 1a y 1b ---
                    // TODO: Intersectar la 'parcelaRegion' (Área Neta) con las zonas de pendiente válida
                    // El resultado será el 'Mapa_Valido' final.


                    tr.Commit(); // Confirmamos los cambios (aunque aún no hemos escrito nada)
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n¡Error durante el procesamiento! {ex.Message}");
                    tr.Abort(); // Deshacer cualquier cambio si hay un error
                }
            } // La transacción se cierra aquí

            ed.WriteMessage("\n--- PROCESO FASE 1 TERMINADO (Lógica principal pendiente) ---");
        } // Cierre del método RunPhase1()
    } // Cierre de la clase Phase1Commands
} // Cierre del namespace Civil3D_Phase1
