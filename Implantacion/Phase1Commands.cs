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
using Autodesk.Civil.DatabaseServices.Styles; // Necesario para el análisis de estilos

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
                ed.WriteMessage("\n--- Plugin Fase 1 (v5 - Análisis Pendiente) cargado. ---");
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
            CivilDocument cdoc = CivilApplication.ActiveDocument; // Necesario para el terreno

            // --- CAMBIO DE VERSIÓN AQUÍ ---
            ed.WriteMessage("\n--- Ejecutando FASE1 (VERSIÓN v5 - Análisis Pendiente) ---");

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
                Region parcelaRegion = null; // Región del Área Neta 2D
                Region slopeRegionOK = new Region(); // Región de pendientes válidas
                ObjectId debugLayerId = ObjectId.Null;
                BlockTableRecord btr = null;

                try
                {
                    // --- PASO DE DEPURACIÓN: CREAR CAPA ---
                    ed.WriteMessage("\nDEBUG: Creando capa 'DEBUG_FLAT'...");
                    debugLayerId = CreateLayer(db, "DEBUG_FLAT", Color.FromColorIndex(ColorMethod.ByAci, 1)); // Color Rojo
                    BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    btr = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                    // --- PASO 1a: ANÁLISIS 2D (PARCELA - AFECCIONES) ---
                    ed.WriteMessage("\nIniciando Paso 1a: Cálculo del Área Neta...");
                    Autodesk.AutoCAD.DatabaseServices.Polyline parcelaOriginal = tr.GetObject(parcelaId, OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.Polyline;
                    if (parcelaOriginal == null || !parcelaOriginal.Closed)
                    {
                        ed.WriteMessage("\nError: La polilínea de parcela no es válida o no está cerrada. Abortando.");
                        tr.Abort(); return;
                    }
                    
                    Autodesk.AutoCAD.DatabaseServices.Polyline parcelaPlana = AplanarPolyline(parcelaOriginal);
                    parcelaPlana.LayerId = debugLayerId;
                    btr.AppendEntity(parcelaPlana);
                    tr.AddNewlyCreatedDBObject(parcelaPlana, true);

                    try
                    {
                        Autodesk.AutoCAD.DatabaseServices.DBObjectCollection parcelaCurves = new Autodesk.AutoCAD.DatabaseServices.DBObjectCollection { parcelaPlana };
                        parcelaRegion = Autodesk.AutoCAD.DatabaseServices.Region.CreateFromCurves(parcelaCurves)[0] as Autodesk.AutoCAD.DatabaseServices.Region;
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\n¡ERROR CRÍTICO! La polilínea de PARCELA tiene geometría inválida. {ex.Message}");
                        tr.Abort(); return;
                    }
                    
                    int i = 1;
                    foreach (ObjectId afeccionId in afeccionesIds)
                    {
                        Autodesk.AutoCAD.DatabaseServices.Polyline afeccionOriginal = tr.GetObject(afeccionId, OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.Polyline;
                        if (afeccionOriginal == null) { i++; continue; }

                        Autodesk.AutoCAD.DatabaseServices.Polyline afeccionPlana = AplanarPolyline(afeccionOriginal);
                        afeccionPlana.LayerId = debugLayerId;
                        btr.AppendEntity(afeccionPlana);
                        tr.AddNewlyCreatedDBObject(afeccionPlana, true);

                        try
                        {
                            Autodesk.AutoCAD.DatabaseServices.DBObjectCollection afeccionCurves = new Autodesk.AutoCAD.DatabaseServices.DBObjectCollection { afeccionPlana };
                            Autodesk.AutoCAD.DatabaseServices.Region afeccionRegion = Autodesk.AutoCAD.DatabaseServices.Region.CreateFromCurves(afeccionCurves)[0] as Autodesk.AutoCAD.DatabaseServices.Region;
                            parcelaRegion.BooleanOperation(Autodesk.AutoCAD.DatabaseServices.BooleanOperationType.BoolSubtract, afeccionRegion);
                        }
                        catch (System.Exception ex)
                        {
                            ed.WriteMessage($"\n¡AVISO! La Afección {i} tiene geometría inválida y será IGNORADA. {ex.Message}");
                        }
                        i++;
                    }
                    
                    ed.WriteMessage("\nÁrea Neta 2D (Región) calculada con éxito.");

                    // --- PASO 1b: ANÁLISIS 3D (PENDIENTE N-S <= 15%) ---
                    ed.WriteMessage("\nIniciando Paso 1b: Análisis de Pendiente del Terreno...");
                    TinSurface terreno = tr.GetObject(terrenoId, OpenMode.ForRead) as TinSurface;
                    
                    // Definimos los rangos de pendiente. Queremos de 0% a 15%
                    SurfaceAnalysisSlopeRange[] slopeRanges = new SurfaceAnalysisSlopeRange[]
                    {
                        // Rango 1: 0% a 15% (Este es el que nos interesa)
                        new SurfaceAnalysisSlopeRange(0.0, 15.0),
                        // Rango 2: > 15% (El resto)
                        new SurfaceAnalysisSlopeRange(15.0, 9999.0)
                    };
                    
                    // Obtenemos los polígonos del análisis. IMPORTANTE: Usamos 'SurfaceAnalysisDirection.North'
                    ObjectIdCollection polyIds = terreno.Analysis.GetSlopeData(slopeRanges, SurfaceAnalysisDirection.North);

                    // La API nos devuelve polígonos para TODOS los rangos.
                    // El primer rango (índice 0) corresponde a 0-15%.
                    // Los polígonos de ese rango están en polyIds[0]
                    ObjectId polyIdRange1 = polyIds[0];
                    DBObject polyObj = tr.GetObject(polyIdRange1, OpenMode.ForRead);
                    
                    // Este objeto es un "contenedor" de polilíneas (Polyline3d)
                    if (polyObj is Polyline3d)
                    {
                        // Si es una sola polilínea, la procesamos
                        Polyline3d p3d = polyObj as Polyline3d;
                        Polyline p2d = AplanarPolyline(p3d.ToPolyline()); // La aplanamos
                        p2d.LayerId = debugLayerId;
                        p2d.ColorIndex = 2; // Amarillo
                        btr.AppendEntity(p2d);
                        tr.AddNewlyCreatedDBObject(p2d, true);

                        Region regionValida = Region.CreateFromCurves(new DBObjectCollection { p2d })[0] as Region;
                        slopeRegionOK.BooleanOperation(BooleanOperationType.BoolUnite, regionValida);
                    }
                    else if (polyObj is DBObjectCollection)
                    {
                        // Si es una colección de polilíneas (lo más común)
                        DBObjectCollection polyCollection = polyObj as DBObjectCollection;
                        ed.WriteMessage($"\nDEBUG: Encontradas {polyCollection.Count} zonas de pendiente válida (0-15%).");
                        foreach (DBObject obj in polyCollection)
                        {
                            Polyline3d p3d = obj as Polyline3d;
                            if (p3d == null) continue;
                            
                            Polyline p2d = AplanarPolyline(p3d.ToPolyline()); // La aplanamos
                            p2d.LayerId = debugLayerId;
                            p2d.ColorIndex = 2; // Amarillo
                            btr.AppendEntity(p2d);
                            tr.AddNewlyCreatedDBObject(p2d, true);

                            // Creamos una región para esta zona y la "unimos" a la región total de pendientes
                            Region regionValida = Region.CreateFromCurves(new DBObjectCollection { p2d })[0] as Region;
                            slopeRegionOK.BooleanOperation(BooleanOperationType.BoolUnite, regionValida);
                        }
                    }
                    
                    ed.WriteMessage("\nZonas de pendiente válida (<15% N-S) procesadas.");
                    
                    // --- PASO 1c: INTERSECCIÓN 2D y 3D ---
                    ed.WriteMessage("\nIniciando Paso 1c: Creando Mapa de Validez (Área Neta Y Pendiente Válida)...");
                    
                    // 'parcelaRegion' = Área Neta 2D
                    // 'slopeRegionOK' = Áreas con pendiente N-S < 15%
                    // Hacemos la intersección de ambas.
                    parcelaRegion.BooleanOperation(BooleanOperationType.BoolIntersect, slopeRegionOK);

                    // 'parcelaRegion' AHORA CONTIENE EL "MAPA VÁLIDO" FINAL
                    
                    ed.WriteMessage("\n¡Mapa de Validez final calculado con éxito!");
                    
                    // Dibujamos el resultado final en Verde
                    parcelaRegion.LayerId = debugLayerId;
                    parcelaRegion.ColorIndex = 3; // Color Verde
                    btr.AppendEntity(parcelaRegion);
                    tr.AddNewlyCreatedDBObject(parcelaRegion, true);
                    ed.WriteMessage("\nDEBUG: Mapa de Validez final dibujado en capa 'DEBUG_FLAT'.");

                    // --- PASO 2: Bucle de Optimización ---
                    ed.WriteMessage("\n(TODO: Implementar Bucle de Optimización E-O)");

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
