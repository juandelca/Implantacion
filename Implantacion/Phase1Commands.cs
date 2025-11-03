/* --- Dependencias de .NET --- */
using System;
using System.Collections.Generic;
using System.Linq; // Necesario para ordenar los resultados

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
using Autodesk.Civil.DatabaseServices.Styles; // Esta línea está bien y funciona

[assembly: CommandClass(typeof(Civil3D_Phase1.Phase1Commands))]

namespace Civil3D_Phase1
{
    // --- CLASE AUXILIAR PARA GUARDAR RESULTADOS ---
    public class LayoutResult
    {
        public double Offset { get; set; }
        public int TotalStrings { get; set; }
        public int TrackersLargos { get; set; }
        public int TrackersCortos { get; set; }

        public LayoutResult(double offset, int totalStrings, int largos, int cortos)
        {
            Offset = offset;
            TotalStrings = totalStrings;
            TrackersLargos = largos;
            TrackersCortos = cortos;
        }
    }

    
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
                ed.WriteMessage("\n--- Plugin Fase 1 (v17 - Corrección Intersección) cargado. ---");
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
        // --- Constantes del Proyecto ---
        const double PITCH = 10.0; // 10m
        const double LONGITUD_LARGA = 37.7; // (2 strings)
        const double LONGITUD_CORTA = 17.4; // (1 string)
        const int OBJETIVO_STRINGS = 400;


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

        // --- FUNCIÓN AUXILIAR PARA APLANAR Polyline (para inputs de usuario) ---
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

        // --- FUNCIÓN AUXILIAR PARA APLANAR Polyline3d ---
        private Polyline AplanarPolyline3d(Polyline3d p3d, Transaction tr)
        {
            Polyline polyPlana = new Polyline();
            polyPlana.Normal = Vector3d.ZAxis;
            polyPlana.Elevation = 0.0;
            foreach (ObjectId vertexId in p3d)
            {
                Autodesk.AutoCAD.DatabaseServices.DBObject vtxObj = tr.GetObject(vertexId, OpenMode.ForRead);
                if (vtxObj is PolylineVertex3d)
                {
                    PolylineVertex3d vtx = vtxObj as PolylineVertex3d;
                    Point2d pt2d = new Point2d(vtx.Position.X, vtx.Position.Y);
                    polyPlana.AddVertexAt(polyPlana.NumberOfVertices, pt2d, 0, 0, 0);
                }
            }
            polyPlana.Closed = p3d.Closed;
            return polyPlana;
        }


        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        [CommandMethod("FASE1")]
        public void RunPhase1()
        {
            // --- 0. OBTENER DOCUMENTOS Y EDITOR ---
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            CivilDocument cdoc = CivilApplication.ActiveDocument;

            ed.WriteMessage("\n--- Ejecutando FASE1 (VERSIÓN v17 - Corrección Intersección) ---");

            // --- 1. SELECCIÓN DE OBJETOS (INPUTS) ---
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
            
            // --- Declaraciones fuera de la transacción ---
            List<LayoutResult> todosLosResultados = new List<LayoutResult>();
            Region mapaValido = new Region(); // El 'Mapa Válido' final (Región)

            // --- 2. TRANSACCIÓN PARA PASO 1 (CÁLCULO DE MAPA VÁLIDO) ---
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    // --- PASO DE DEPURACIÓN: CREAR CAPA ---
                    ed.WriteMessage("\nDEBUG: Creando capa 'DEBUG_FLAT'...");
                    ObjectId debugLayerId = CreateLayer(db, "DEBUG_FLAT", Color.FromColorIndex(ColorMethod.ByAci, 1)); // Color Rojo
                    BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    BlockTableRecord btr = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                    // --- PASO 1a: ANÁLISIS 2D (PARCELA - AFECCIONES) ---
                    ed.WriteMessage("\nIniciando Paso 1a: Cálculo del Área Neta...");
                    Autodesk.AutoCAD.DatabaseServices.Polyline parcelaOriginal = tr.GetObject(parcelaId, OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.Polyline;
                    if (parcelaOriginal == null || !parcelaOriginal.Closed)
                    {
                        ed.WriteMessage("\nError: La polilínea de parcela no es válida o no está cerrada. Abortando.");
                        tr.Abort(); return;
                    }
                    
                    Autodesk.AutoCAD.DatabaseServices.Polyline parcelaPlana = AplanarPolyline(parcelaOriginal);
                    
                    try
                    {
                        Autodesk.AutoCAD.DatabaseServices.DBObjectCollection parcelaCurves = new Autodesk.AutoCAD.DatabaseServices.DBObjectCollection { parcelaPlana };
                        // Asignamos el resultado a nuestra variable 'mapaValido'
                        mapaValido = Autodesk.AutoCAD.DatabaseServices.Region.CreateFromCurves(parcelaCurves)[0] as Autodesk.AutoCAD.DatabaseServices.Region;
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
                        try
                        {
                            Autodesk.AutoCAD.DatabaseServices.DBObjectCollection afeccionCurves = new Autodesk.AutoCAD.DatabaseServices.DBObjectCollection { afeccionPlana };
                            Autodesk.AutoCAD.DatabaseServices.Region afeccionRegion = Autodesk.AutoCAD.DatabaseServices.Region.CreateFromCurves(afeccionCurves)[0] as Autodesk.AutoCAD.DatabaseServices.Region;
                            mapaValido.BooleanOperation(Autodesk.AutoCAD.DatabaseServices.BooleanOperationType.BoolSubtract, afeccionRegion);
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
                    Region slopeRegionOK = new Region(); 
                    TinSurface terreno = tr.GetObject(terrenoId, OpenMode.ForRead) as TinSurface;
                    SurfaceAnalysisSlopeRange[] slopeRanges = new SurfaceAnalysisSlopeRange[]
                    {
                        new SurfaceAnalysisSlopeRange(0.0, 15.0),
                        new SurfaceAnalysisSlopeRange(15.0, 9999.0)
                    };
                    ObjectIdCollection polyIds = terreno.Analysis.GetSlopeData(slopeRanges, SurfaceAnalysisDirection.North);
                    ObjectId polyIdRange1 = polyIds[0];
                    Autodesk.AutoCAD.DatabaseServices.DBObject polyObj = tr.GetObject(polyIdRange1, OpenMode.ForRead);
                    
                    Autodesk.AutoCAD.DatabaseServices.DBObjectCollection polyCollection = new Autodesk.AutoCAD.DatabaseServices.DBObjectCollection();
                    if (polyObj is Polyline3d) { polyCollection.Add(polyObj); }
                    else if (polyObj is Autodesk.AutoCAD.DatabaseServices.DBObjectCollection)
                    { polyCollection = polyObj as Autodesk.AutoCAD.DatabaseServices.DBObjectCollection; }

                    ed.WriteMessage($"\nDEBUG: Encontradas {polyCollection.Count} zonas de pendiente válida (0-15%).");
                    foreach (Autodesk.AutoCAD.DatabaseServices.DBObject obj in polyCollection)
                    {
                        Polyline3d p3d = obj as Polyline3d;
                        if (p3d == null) continue;
                        Polyline p2d = AplanarPolyline3d(p3d, tr); 
                        p2d.LayerId = debugLayerId;
                        p2d.ColorIndex = 2; // Amarillo
                        btr.AppendEntity(p2d);
                        tr.AddNewlyCreatedDBObject(p2d, true);

                        try
                        {
                            Region regionValida = Region.CreateFromCurves(new Autodesk.AutoCAD.DatabaseServices.DBObjectCollection { p2d })[0] as Region;
                            slopeRegionOK.BooleanOperation(BooleanOperationType.BoolUnite, regionValida);
                        }
                        catch (System.Exception ex)
                        {
                             ed.WriteMessage($"\n¡AVISO! Una zona de pendiente tiene geometría inválida y será IGNORADA. {ex.Message}");
                        }
                    }
                    ed.WriteMessage("\nZonas de pendiente válida (<15% N-S) procesadas.");
                    
                    // --- PASO 1c: INTERSECCIÓN 2D y 3D ---
                    ed.WriteMessage("\nIniciando Paso 1c: Creando Mapa de Validez (Área Neta Y Pendiente Válida)...");
                    mapaValido.BooleanOperation(BooleanOperationType.BoolIntersect, slopeRegionOK);
                    ed.WriteMessage("\n¡Mapa de Validez final calculado con éxito!");
                    
                    mapaValido.LayerId = debugLayerId;
                    mapaValido.ColorIndex = 3; // Color Verde
                    btr.AppendEntity(mapaValido);
                    tr.AddNewlyCreatedDBObject(mapaValido, true);
                    ed.WriteMessage("\nDEBUG: Mapa de Validez final dibujado en capa 'DEBUG_FLAT'.");

                    tr.Commit();
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n¡Error Inesperado en Paso 1! {ex.Message} {ex.StackTrace}");
                    tr.Abort();
                    return; // Salir si el Paso 1 falla
                }
            } // La transacción se cierra aquí

            // --- PASO 2: BUCLE DE OPTIMIZACIÓN (E-O) ---
            ed.WriteMessage("\n--- Iniciando Paso 2: Bucle de Optimización (100 iteraciones) ---");

            try
            {
                // Obtenemos los límites (Bounding Box) de nuestro mapa válido
                Extents3d bounds = mapaValido.Bounds.Value;
                Point3d minPt = bounds.MinPoint;
                Point3d maxPt = bounds.MaxPoint;

                for (int i = 0; i < 100; i++)
                {
                    double currentOffset = Math.Round(i * 0.1, 2); 
                    double startX = minPt.X + currentOffset;
                    
                    int totalStrings = 0;
                    int totalLargos = 0;
                    int totalCortos = 0;
                    
                    for (double currentX = startX; currentX <= maxPt.X; currentX += PITCH)
                    {
                        Line ejeVertical = new Line(
                            new Point3d(currentX, minPt.Y - 100.0, 0),
                            new Point3d(currentX, maxPt.Y + 100.0, 0)
                        );

                        // --- INICIO DE LA CORRECCIÓN (v17) ---
                        // CORRECCIÓN 1: Usar 'Point3dCollection'
                        Point3dCollection intersectionPoints = new Point3dCollection();

                        mapaValido.IntersectWith(ejeVertical, Intersect.OnBothOperands, intersectionPoints, IntPtr.Zero, IntPtr.Zero);

                        if (intersectionPoints.Count > 0 && intersectionPoints.Count % 2 == 0)
                        {
                            // CORRECCIÓN 2: Iterar directamente sobre la Point3dCollection
                            List<Point3d> puntosOrdenados = new List<Point3d>();
                            foreach (Point3d pt in intersectionPoints)
                            {
                                puntosOrdenados.Add(pt);
                            }
                            puntosOrdenados = puntosOrdenados.OrderBy(p => p.Y).ToList();
                            // --- FIN DE LA CORRECCIÓN (v17) ---

                            for (int s = 0; s < puntosOrdenados.Count; s += 2)
                            {
                                Point3d p1 = puntosOrdenados[s];
                                Point3d p2 = puntosOrdenados[s + 1];
                                double segmentLength = p1.DistanceTo(p2);
                                
                                int numLargos = (int)Math.Floor(segmentLength / LONGITUD_LARGA);
                                double remainingLength = segmentLength - (numLargos * LONGITUD_LARGA);
                                int numCortos = (int)Math.Floor(remainingLength / LONGITUD_CORTA);

                                totalLargos += numLargos;
                                totalCortos += numCortos;
                                totalStrings += (numLargos * 2) + (numCortos * 1);
                            }
                        }
                        
                        intersectionPoints.Dispose(); // Liberar memoria
                        ejeVertical.Dispose(); // Liberar memoria de la línea
                    } 

                    todosLosResultados.Add(new LayoutResult(currentOffset, totalStrings, totalLargos, totalCortos));
                    ed.WriteMessage($"Offset {currentOffset.ToString("F1")}m: {totalStrings} strings ({totalLargos} largos, {totalCortos} cortos)");
                
                } // Fin del bucle de offset (100 iteraciones)

                ed.WriteMessage("\n--- Bucle de Optimización Terminado ---");
                
                // --- PASO 3: SELECCIONAR EL GANADOR ---
                ed.WriteMessage("\n--- Iniciando Paso 3: Seleccionando Layout Ganador ---");

                LayoutResult ganador = null;

                List<LayoutResult> layoutsPerfectos = todosLosResultados
                    .Where(r => r.TotalStrings == OBJETIVO_STRINGS)
                    .ToList();

                if (layoutsPerfectos.Count > 0)
                {
                    ganador = layoutsPerfectos.OrderByDescending(r => r.TrackersLargos).First();
                    ed.WriteMessage($"\n¡OBJETIVO ALCANZADO! Se encontró un layout con {OBJETIVO_STRINGS} strings.");
                }
                else
                {
                    ed.WriteMessage($"\nAVISO: No se alcanzó el objetivo de {OBJETIVO_STRINGS} strings.");
                    
                    ganador = todosLosResultados
                        .Where(r => r.TotalStrings < OBJETIVO_STRINGS) 
                        .OrderByDescending(r => r.TotalStrings)     
                        .ThenByDescending(r => r.TrackersLargos)   
                        .FirstOrDefault(); 
                }

                if (ganador != null)
                {
                    ed.WriteMessage("\n--- LAYOUT GANADOR SELECCIONADO ---");
                    ed.WriteMessage($"Offset (E-O): {ganador.Offset.ToString("F1")}m");
                    ed.WriteMessage($"Total Strings: {ganador.TotalStrings}");
                    ed.WriteMessage($"Trackers Largos (37.7m): {ganador.TrackersLargos}");
                    ed.WriteMessage($"Trackers Cortos (17.4m): {ganador.TrackersCortos}");
                }
                else
                {
                    ed.WriteMessage("\nERROR: No se pudo seleccionar ningún layout ganador (todos dieron 0 strings o más de 400).");
                }
                
                // --- PASO 4: DIBUJAR EL RESULTADO ---
                ed.WriteMessage("\n(TODO: Implementar el dibujado del layout ganador)");

            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n¡Error Inesperado durante el Bucle de Optimización! {ex.Message} {ex.StackTrace}");
            }
            
            // Liberamos la región del mapa válido de la memoria
            mapaValido.Dispose();

            ed.WriteMessage("\n--- PROCESO FASE 1 TERMINADO ---");
        } // Cierre del método RunPhase1()
    } // Cierre de la clase Phase1Commands
} // Cierre del namespace Civil3D_Phase1
