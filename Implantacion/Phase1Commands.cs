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
// (No necesitamos 'Styles' en esta versión)

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
                ed.WriteMessage("\n--- Plugin Fase 1 (v16 - Bucle Optimización) cargado. ---");
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
        // (Según tu "plano" original)
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

            ed.WriteMessage("\n--- Ejecutando FASE1 (VERSIÓN v16 - Bucle Optimización) ---");

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

            ed.WriteMessage("\n--- Todos los inputs han sido seleccionados. ---");

            // --- 2. TRANSACCIÓN PARA PROCESAR LOS DATOS ---
            
            // Lista para guardar los 100 resultados del bucle
            List<LayoutResult> todosLosResultados = new List<LayoutResult>();
            Region mapaValido = new Region(); // El 'Mapa Válido' final (Región)

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    // --- PASO DE DEPURACIÓN: CREAR CAPA ---
                    ed.WriteMessage("\nDEBUG: Creando capa 'DEBUG_FLAT'...");
                    ObjectId debugLayerId = CreateLayer(db, "DEBUG_FLAT", Color.FromColorIndex(ColorMethod.ByAci, 1)); // Color Rojo
                    BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    BlockTableRecord btr = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                    // --- PASO 1: CÁLCULO DEL MAPA VÁLIDO (SOLO 2D) ---
                    ed.WriteMessage("\nIniciando Paso 1: Cálculo del Área Neta...");
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
                    ed.WriteMessage("\n¡Mapa de Validez (Solo 2D) calculado con éxito!");
                    
                    // Dibujamos el mapa válido final para depuración
                    Region mapaValidoDebug = mapaValido.Clone() as Region;
                    mapaValidoDebug.LayerId = debugLayerId;
                    mapaValidoDebug.ColorIndex = 3; // Color Verde
                    btr.AppendEntity(mapaValidoDebug);
                    tr.AddNewlyCreatedDBObject(mapaValidoDebug, true);
                    
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
            // Esto se hace FUERA de la transacción de lectura/escritura
            
            ed.WriteMessage("\n--- Iniciando Paso 2: Bucle de Optimización (100 iteraciones) ---");

            try
            {
                // Obtenemos los límites (Bounding Box) de nuestro mapa válido
                Extents3d bounds = mapaValido.Bounds.Value;
                Point3d minPt = bounds.MinPoint;
                Point3d maxPt = bounds.MaxPoint;

                // Bucle de 100 iteraciones (0.0m a 9.9m, incremento de 10cm)
                for (int i = 0; i < 100; i++)
                {
                    double currentOffset = Math.Round(i * 0.1, 2); // Offset actual (ej: 0.0, 0.1, 0.2 ...)
                    double startX = minPt.X + currentOffset;
                    
                    int totalStrings = 0;
                    int totalLargos = 0;
                    int totalCortos = 0;
                    
                    // Creamos la rejilla N-S para ESTE offset
                    for (double currentX = startX; currentX <= maxPt.X; currentX += PITCH)
                    {
                        // Creamos un eje N-S (una línea vertical muy larga)
                        Line ejeVertical = new Line(
                            new Point3d(currentX, minPt.Y - 100.0, 0),
                            new Point3d(currentX, maxPt.Y + 100.0, 0)
                        );

                        // Encontramos TODOS los puntos donde este eje intersecta el Mapa Válido
                        DBObjectCollection intersectionPoints = new DBObjectCollection();
                        // Usamos 'using' para asegurarnos de que los objetos de intersección se liberen
                        using (intersectionPoints)
                        {
                            mapaValido.IntersectWith(ejeVertical, Intersect.OnBothOperands, intersectionPoints, IntPtr.Zero, IntPtr.Zero);
                        }

                        // Si hay puntos de intersección (debe ser un número par: 2, 4, 6...)
                        if (intersectionPoints.Count > 0 && intersectionPoints.Count % 2 == 0)
                        {
                            // Ordenamos los puntos por su coordenada Y (de sur a norte)
                            List<Point3d> puntosOrdenados = new List<Point3d>();
                            foreach (Autodesk.AutoCAD.DatabaseServices.DBObject obj in intersectionPoints)
                            {
                                if (obj is Point)
                                {
                                    puntosOrdenados.Add(((Point)obj).Position);
                                }
                            }
                            puntosOrdenados = puntosOrdenados.OrderBy(p => p.Y).ToList();

                            // Iteramos por los segmentos (par por par)
                            for (int s = 0; s < puntosOrdenados.Count; s += 2)
                            {
                                Point3d p1 = puntosOrdenados[s];
                                Point3d p2 = puntosOrdenados[s + 1];

                                double segmentLength = p1.DistanceTo(p2);
                                
                                // Lógica de "llenado"
                                int numLargos = (int)Math.Floor(segmentLength / LONGITUD_LARGA);
                                double remainingLength = segmentLength - (numLargos * LONGITUD_LARGA);
                                int numCortos = (int)Math.Floor(remainingLength / LONGITUD_CORTA);

                                // Acumulamos los totales de ESTE offset
                                totalLargos += numLargos;
                                totalCortos += numCortos;
                                totalStrings += (numLargos * 2) + (numCortos * 1);
                            }
                        }
                        
                        ejeVertical.Dispose(); // Liberar memoria de la línea
                    } // Fin del bucle de rejilla (eje X)

                    // Guardamos el resultado de esta iteración de offset
                    todosLosResultados.Add(new LayoutResult(currentOffset, totalStrings, totalLargos, totalCortos));
                    ed.WriteMessage($"Offset {currentOffset.ToString("F1")}m: {totalStrings} strings ({totalLargos} largos, {totalCortos} cortos)");
                
                } // Fin del bucle de offset (100 iteraciones)

                ed.WriteMessage("\n--- Bucle de Optimización Terminado ---");
                
                // --- PASO 3: SELECCIONAR EL GANADOR ---
                ed.WriteMessage("\n--- Iniciando Paso 3: Seleccionando Layout Ganador ---");

                LayoutResult ganador = null;

                // Regla 1 (Objetivo): ¿Hay alguno que sume 400?
                List<LayoutResult> layoutsPerfectos = todosLosResultados
                    .Where(r => r.TotalStrings == OBJETIVO_STRINGS)
                    .ToList();

                if (layoutsPerfectos.Count > 0)
                {
                    // Regla 2 (Prioridad): Si hay varios con 400, elegimos el que tenga más trackers largos
                    ganador = layoutsPerfectos.OrderByDescending(r => r.TrackersLargos).First();
                    ed.WriteMessage($"\n¡OBJETIVO ALCANZADO! Se encontró un layout con {OBJETIVO_STRINGS} strings.");
                }
                else
                {
                    // Regla 3 (Sub-óptimo): Si NINGUNO llega a 400, elegimos el que más se acerque por debajo
                    ed.WriteMessage($"\nAVISO: No se alcanzó el objetivo de {OBJETIVO_STRINGS} strings.");
                    
                    ganador = todosLosResultados
                        .Where(r => r.TotalStrings < OBJETIVO_STRINGS) // Solo los que estén por debajo
                        .OrderByDescending(r => r.TotalStrings)     // El que tenga más strings
                        .ThenByDescending(r => r.TrackersLargos)   // Y de esos, el que tenga más largos
                        .FirstOrDefault(); // Coge el primero
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

            ed.WriteMessage("\n--- PROCESO FASE 1 TERMINADO ---");
        } // Cierre del método RunPhase1()
    } // Cierre de la clase Phase1Commands
} // Cierre del namespace Civil3D_Phase1
