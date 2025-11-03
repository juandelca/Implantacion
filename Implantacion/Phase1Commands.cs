/* --- Dependencias de .NET --- */
using System;
using System.Collections.Generic;

/* --- Dependencias de AutoCAD --- */
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors; // Necesario para los colores de capa

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
                // --- CAMBIO DE VERSIÓN AQUÍ ---
                ed.WriteMessage("\n--- Plugin Fase 1 (v4 - Control Geometría) cargado. ---");
                ed.WriteMessage("\n--- Escriba 'FASE1' para ejecutar. ---");
            }
        }

        public void Terminate() { }
    }

    // -----------------------------------------------------------------
    // CLASE DE COMANDOS
    // -----------------------------------------------------------------
    public class Phase1Commands
    {
        // --- FUNCIÓN AUXILIAR PARA CREAR CAPAS ---
        private ObjectId CreateLayer(Database db, string layerName, Color color)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                LayerTable lt = tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
                if (lt.Has(layerName)) return lt[layerName];
                lt.UpgradeOpen();
                LayerTableRecord ltr = new LayerTableRecord();
                ltr.Name = layerName;
                ltr.Color = color;
                ObjectId layerId = lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
                tr.Commit();
                return layerId;
            }
        }

        // --- FUNCIÓN AUXILIAR PARA APLANAR POLILÍNEAS ---
        private Polyline AplanarPolyline(Polyline polyOriginal)
        {
            Polyline polyPlana = new Polyline();
            polyPlana.Normal = Vector3d.ZAxis;
            polyPlana.Elevation = 0.0;
            for (int i = 0; i < polyOriginal.NumberOfVertices; i++)
            {
                polyPlana.AddVertexAt(i, polyOriginal.GetPoint2dAt(i), polyOriginal.GetBulgeAt(i), 0.0, 0.0);
            }
            polyPlana.Closed = true;
            return polyPlana;
        }


        [CommandMethod("FASE1")]
        public void RunPhase1()
        {
            // --- 0. OBTENER DOCUMENTOS Y EDITOR ---
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // --- CAMBIO DE VERSIÓN AQUÍ ---
            ed.WriteMessage("\n--- Ejecutando FASE1 (VERSIÓN v4 - Control Geometría) ---");

            // --- 1. SELECCIÓN DE OBJETOS (INPUTS) ---
            // (El código de selección es idéntico al anterior)
            PromptEntityOptions peoParcela = new PromptEntityOptions("\nSeleccione la Polilínea de la Parcela: ");
            peoParcela.SetRejectMessage("\nEl objeto seleccionado no es una Polilínea. Inténtelo de nuevo.");
            peoParcela.AddAllowedClass(typeof(Autodesk.AutoCAD.DatabaseServices.Polyline), true);
            PromptEntityResult perParcela = ed.GetEntity(peoParcela);
            if (perParcela.Status != PromptStatus.OK) { ed.WriteMessage("\n*Cancelado*"); return; }
            ObjectId parcelaId = perParcela.ObjectId;
            ed.WriteMessage("\nParcela seleccionada.");

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
                Region parcelaRegion = null; // La declaramos fuera para que sea accesible
                ObjectId debugLayerId = ObjectId.Null;

                try
                {
                    // --- PASO DE DEPURACIÓN: CREAR CAPA ---
                    ed.WriteMessage("\nDEBUG: Creando capa 'DEBUG_FLAT'...");
                    debugLayerId = CreateLayer(db, "DEBUG_FLAT", Color.FromColorIndex(ColorMethod.ByAci, 1)); // Color Rojo
                    BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    BlockTableRecord btr = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                    // --- PASO 1a: ANÁLISIS 2D (PARCELA) ---
                    ed.WriteMessage("\nIniciando Paso 1a: Cálculo del Área Neta (Parcela)...");
                    Autodesk.AutoCAD.DatabaseServices.Polyline parcelaOriginal = tr.GetObject(parcelaId, OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.Polyline;
                    if (parcelaOriginal == null || !parcelaOriginal.Closed)
                    {
                        ed.WriteMessage("\nError: La polilínea de parcela no es válida o no está cerrada. Abortando.");
                        tr.Abort(); return;
                    }
                    
                    ed.WriteMessage("\nDEBUG: Aplanando polilínea de parcela...");
                    Autodesk.AutoCAD.DatabaseServices.Polyline parcelaPlana = AplanarPolyline(parcelaOriginal);
                    
                    ed.WriteMessage("\nDEBUG: Dibujando parcela aplanada en capa 'DEBUG_FLAT'...");
                    parcelaPlana.LayerId = debugLayerId;
                    btr.AppendEntity(parcelaPlana);
                    tr.AddNewlyCreatedDBObject(parcelaPlana, true);

                    // --- INICIO DE CONTROL DE GEOMETRÍA (PARCELA) ---
                    try
                    {
                        ed.WriteMessage("\nDEBUG: Creando región de parcela...");
                        Autodesk.AutoCAD.DatabaseServices.DBObjectCollection parcelaCurves = new Autodesk.AutoCAD.DatabaseServices.DBObjectCollection { parcelaPlana };
                        parcelaRegion = Autodesk.AutoCAD.DatabaseServices.Region.CreateFromCurves(parcelaCurves)[0] as Autodesk.AutoCAD.DatabaseServices.Region;
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\n¡ERROR CRÍTICO! La polilínea de PARCELA tiene geometría inválida (posible auto-intersección). {ex.Message}");
                        ed.WriteMessage("\nComando abortado.");
                        tr.Abort();
                        return;
                    }
                    // --- FIN DE CONTROL DE GEOMETRÍA (PARCELA) ---
                    
                    // --- PASO 1a: ANÁLISIS 2D (AFECCIONES) ---
                    int i = 1;
                    foreach (ObjectId afeccionId in afeccionesIds)
                    {
                        Autodesk.AutoCAD.DatabaseServices.Polyline afeccionOriginal = tr.GetObject(afeccionId, OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.Polyline;
                        if (afeccionOriginal == null)
                        {
                            ed.WriteMessage($"\nAVISO: Objeto {i} no es una polilínea válida, ignorando.");
                            i++;
                            continue; // Salta al siguiente objeto
                        }

                        ed.WriteMessage($"\nDEBUG: Aplanando afección {i}...");
                        Autodesk.AutoCAD.DatabaseServices.Polyline afeccionPlana = AplanarPolyline(afeccionOriginal);
                        
                        ed.WriteMessage($"\nDEBUG: Dibujando afección aplanada {i}...");
                        afeccionPlana.LayerId = debugLayerId;
                        btr.AppendEntity(afeccionPlana);
                        tr.AddNewlyCreatedDBObject(afeccionPlana, true);

                        // --- INICIO DE CONTROL DE GEOMETRÍA (AFECCIÓN) ---
                        try
                        {
                            Autodesk.AutoCAD.DatabaseServices.DBObjectCollection afeccionCurves = new Autodesk.AutoCAD.DatabaseServices.DBObjectCollection { afeccionPlana };
                            ed.WriteMessage($"\nDEBUG: Creando región de afección {i}...");
                            Autodesk.AutoCAD.DatabaseServices.Region afeccionRegion = Autodesk.AutoCAD.DatabaseServices.Region.CreateFromCurves(afeccionCurves)[0] as Autodesk.AutoCAD.DatabaseServices.Region;
                            
                            ed.WriteMessage($"\nDEBUG: Restando región de afección {i}...");
                            parcelaRegion.BooleanOperation(Autodesk.AutoCAD.DatabaseServices.BooleanOperationType.BoolSubtract, afeccionRegion);
                        }
                        catch (System.Exception ex)
                        {
                            ed.WriteMessage($"\n¡AVISO! La Afección {i} tiene geometría inválida (posible auto-intersección) y será IGNORADA. {ex.Message}");
                            // No hacemos 'return' ni 'tr.Abort()', simplemente continuamos con la siguiente afección
                        }
                        // --- FIN DE CONTROL DE GEOMETRÍA (AFECCIÓN) ---
                        i++;
                    }
                    
                    ed.WriteMessage("\nÁrea Neta 2D (Región) calculada con éxito.");

                    parcelaRegion.LayerId = debugLayerId;
                    parcelaRegion.ColorIndex = 3; // Color Verde
                    btr.AppendEntity(parcelaRegion);
                    tr.AddNewlyCreatedDBObject(parcelaRegion, true);
                    ed.WriteMessage("\nDEBUG: Región Neta final dibujada en capa 'DEBUG_FLAT'.");

                    // --- PASO 1b: ANÁLISIS 3D (PENDIENTE) ---
                    ed.WriteMessage("\n(TODO: Implementar lógica de análisis de pendiente del terreno)");
                    // ... (resto del código) ...

                    tr.Commit();
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n¡Error Inesperado! {ex.Message} {ex.StackTrace}");
                    tr.Abort();
                }
            } // La transacción se cierra aquí
            ed.WriteMessage("\n--- PROCESO FASE 1 TERMINADO ---");
        } // Cierre del método RunPhase1()
    } // Cierre de la clase Phase1Commands
} // Cierre del namespace Civil3D_Phase1
