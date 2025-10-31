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

        public void Terminate() { }
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

            ed.WriteMessage("\nIniciando Fase 1: Optimización de Layout 2D...");

            // --- 1. SELECCIÓN DE OBJETOS (INPUTS) ---
            
            // --- 1a. Seleccionar la Parcela (Polilínea) ---
            PromptEntityOptions peoParcela = new PromptEntityOptions("\nSeleccione la Polilínea de la Parcela: ");
            peoParcela.SetRejectMessage("\nEl objeto seleccionado no es una Polilínea. Inténtelo de nuevo.");
            peoParcela.AddAllowedClass(typeof(Autodesk.AutoCAD.DatabaseServices.Polyline), true);
            
            PromptEntityResult perParcela = ed.GetEntity(peoParcela);
            if (perParcela.Status != PromptStatus.OK) { ed.WriteMessage("\n*Cancelado*"); return; }
            ObjectId parcelaId = perParcela.ObjectId;
            ed.WriteMessage("\nParcela seleccionada.");

            // --- 1b. Seleccionar las Afecciones (Múltiples Polilíneas) ---
            PromptSelectionOptions psoAfecciones = new PromptSelectionOptions();
            psoAfecciones.MessageForAdding = "\nSeleccione las Polilíneas de Afecciones (o pulse Intro para ninguna): ";
            psoAfecciones.MessageForRemoval = "\nEliminar objetos de la selección: ";
            
            TypedValue[] tvs = new TypedValue[] { new TypedValue((int)DxfCode.Start, "POLYLINE,LWPOLYLINE") };
            SelectionFilter filter = new SelectionFilter(tvs);
            PromptSelectionResult psrAfecciones = ed.GetSelection(psoAfecciones, filter);
            List<ObjectId> afeccionesIds = new List<ObjectId>();

            if (psrAfecciones.Status == PromptStatus.OK)
            {
                afeccionesIds.AddRange(psrAfecciones.Value.GetObjectIds());
                ed.WriteMessage($"\n{afeccionesIds.Count} afecciones seleccionadas.");
            }
            else { ed.WriteMessage("\nNo se seleccionaron afecciones."); }

            // --- 1c. Seleccionar el Terreno Original (TIN Surface) ---
            PromptEntityOptions peoTerreno = new PromptEntityOptions("\nSeleccione la Superficie (Terreno Original): ");
            peoTerreno.SetRejectMessage("\nEl objeto seleccionado no es una Superficie TIN.");
            peoTerreno.AddAllowedClass(typeof(Autodesk.Civil.DatabaseServices.TinSurface), true);
            
            PromptEntityResult perTerreno = ed.GetEntity(peoTerreno);
            if (perTerreno.Status != PromptStatus.OK) { ed.WriteMessage("\n*Cancelado*"); return; }
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

                    Autodesk.AutoCAD.DatabaseServices.Polyline parcelaOriginal = tr.GetObject(parcelaId, OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.Polyline;
                    if (parcelaOriginal == null || !parcelaOriginal.Closed)
                    {
                        ed.WriteMessage("\nError: La polilínea de parcela no es válida o no está cerrada. Abortando.");
                        tr.Abort();
                        return;
                    }

                    // --- INICIO DE LA SOLUCIÓN "RECONSTRUCCIÓN 2D" ---
                    
                    // Función auxiliar para "aplanar" polilíneas
                    Func<Autodesk.AutoCAD.DatabaseServices.Polyline, Autodesk.AutoCAD.DatabaseServices.Polyline> AplanarPolyline = (polyOriginal) =>
                    {
                        Autodesk.AutoCAD.DatabaseServices.Polyline polyPlana = new Autodesk.AutoCAD.DatabaseServices.Polyline();
                        polyPlana.Normal = Vector3d.ZAxis;
                        polyPlana.Elevation = 0.0;

                        for (int i = 0; i < polyOriginal.NumberOfVertices; i++)
                        {
                            // Obtenemos solo el Point2d (X, Y) del vértice
                            Point2d pt2d = polyOriginal.GetPoint2dAt(i);
                            // Obtenemos el "bulge" (curvatura)
                            double bulge = polyOriginal.GetBulgeAt(i);
                            
                            // Añadimos el nuevo vértice 2D a la polilínea plana
                            polyPlana.AddVertexAt(i, pt2d, bulge, 0.0, 0.0);
                        }
                        polyPlana.Closed = true;
                        return polyPlana;
                    };

                    // Aplanamos la parcela
                    Autodesk.AutoCAD.DatabaseServices.Polyline parcelaPlana = AplanarPolyline(parcelaOriginal);
                    
                    Autodesk.AutoCAD.DatabaseServices.DBObjectCollection parcelaCurves = new Autodesk.AutoCAD.DatabaseServices.DBObjectCollection { parcelaPlana };
                    Autodesk.AutoCAD.DatabaseServices.Region parcelaRegion = Autodesk.AutoCAD.DatabaseServices.Region.CreateFromCurves(parcelaCurves)[0] as Autodesk.AutoCAD.DatabaseServices.Region;
                    
                    // Aplanamos y restamos cada afección
                    foreach (ObjectId afeccionId in afeccionesIds)
                    {
                        Autodesk.AutoCAD.DatabaseServices.Polyline afeccionOriginal = tr.GetObject(afeccionId, OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.Polyline;
                        if (afeccionOriginal == null) continue;

                        // Aplanamos la afección
                        Autodesk.AutoCAD.DatabaseServices.Polyline afeccionPlana = AplanarPolyline(afeccionOriginal);
                        
                        Autodesk.AutoCAD.DatabaseServices.DBObjectCollection afeccionCurves = new Autodesk.AutoCAD.DatabaseServices.DBObjectCollection { afeccionPlana };
                        Autodesk.AutoCAD.DatabaseServices.Region afeccionRegion = Autodesk.AutoCAD.DatabaseServices.Region.CreateFromCurves(afeccionCurves)[0] as Autodesk.AutoCAD.DatabaseServices.Region;
                        
                        parcelaRegion.BooleanOperation(Autodesk.AutoCAD.DatabaseServices.BooleanOperationType.BoolSubtract, afeccionRegion);
                    }
                    // --- FIN DE LA SOLUCIÓN ---
                    
                    ed.WriteMessage("\nÁrea Neta 2D (Región) calculada con éxito.");

                    // --- PASO 1b: ANÁLISIS 3D (PENDIENTE) ---
                    ed.WriteMessage("\n(TODO: Implementar lógica de análisis de pendiente del terreno)");

                    // --- PASO 1c: COMBINAR 1a y 1b ---
                    ed.WriteMessage("\n(TODO: Intersectar 'parcelaRegion' con zonas de pendiente válida)");

                    tr.Commit();
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n¡Error durante el procesamiento! {ex.Message} {ex.StackTrace}");
                    tr.Abort();
                }
            } // La transacción se cierra aquí

            ed.WriteMessage("\n--- PROCESO FASE 1 TERMINADO (Lógica principal pendiente) ---");
        } // Cierre del método RunPhase1()
    } // Cierre de la clase Phase1Commands
} // Cierre del namespace Civil3D_Phase1
