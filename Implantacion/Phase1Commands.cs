        // ... (todo el código de 'using...' y la clase 'PluginInitializer' queda igual) ...

        public class Phase1Commands
        {
            [CommandMethod("FASE1")]
            public void RunPhase1()
            {
                // --- 0. OBTENER DOCUMENTOS Y EDITOR ---
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return; // Salir si no hay documento activo
                
                Database db = doc.Database;
                Editor ed = doc.Editor;
                CivilDocument cdoc = CivilApplication.ActiveDocument;

                ed.WriteMessage("\nIniciando Fase 1: Optimización de Layout 2D...");

                // --- 1. SELECCIÓN DE OBJETOS (INPUTS) ---
                
                // --- 1a. Seleccionar la Parcela (Polilínea) ---
                PromptEntityOptions peoParcela = new PromptEntityOptions("\nSeleccione la Polilínea de la Parcela: ");
                peoParcela.SetRejectMessage("\nEl objeto seleccionado no es una Polilínea. Inténtelo de nuevo.");
                peoParcela.AddAllowedClass(typeof(Polyline), true); // Acepta Polyline y Polyline2d/3d
                
                PromptEntityResult perParcela = ed.GetEntity(peoParcela);
                if (perParcela.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n*Cancelado* No se seleccionó la parcela.");
                    return; // Salir del comando
                }
                ObjectId parcelaId = perParcela.ObjectId; // Guardamos el ID de la parcela
                ed.WriteMessage("\nParcela seleccionada.");

                // --- 1b. Seleccionar las Afecciones (Múltiples Polilíneas) ---
                PromptSelectionOptions psoAfecciones = new PromptSelectionOptions();
                psoAfecciones.MessageForAdding = "\nSeleccione las Polilíneas de Afecciones (o pulse Intro para ninguna): ";
                psoAfecciones.MessageForRemoval = "\nEliminar objetos de la selección: ";
                
                // Crear un filtro para aceptar solo Polilíneas
                TypedValue[] tvs = new TypedValue[] {
                    new TypedValue((int)DxfCode.Start, "POLYLINE,LWPOLYLINE")
                };
                SelectionFilter filter = new SelectionFilter(tvs);
                
                PromptSelectionResult psrAfecciones = ed.GetSelection(psoAfecciones, filter);
                List<ObjectId> afeccionesIds = new List<ObjectId>(); // Lista para guardar los IDs

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
                peoTerreno.AddAllowedClass(typeof(TinSurface), true); // Acepta solo Superficies TIN
                
                PromptEntityResult perTerreno = ed.GetEntity(peoTerreno);
                if (perTerreno.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n*Cancelado* No se seleccionó el terreno.");
                    return; // Salir del comando
                }
                ObjectId terrenoId = perTerreno.ObjectId; // Guardamos el ID del terreno
                ed.WriteMessage("\nTerreno seleccionado.");

                ed.WriteMessage("\n--- Todos los inputs han sido seleccionados. ---");


                // --- 2. TRANSACCIÓN PARA LEER LOS DATOS ---
                // Para leer los objetos de la base de datos de AutoCAD,
                // necesitamos abrir una "Transacción".

                // Usamos 'using' para asegurar que la transacción se cierre
                // incluso si hay un error.
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        // TODO: PASO 1: Crear el "Mapa de Validez"
                        // Aquí dentro leeremos la geometría de la Parcela, Afecciones y Terreno
                        // usando los 'ObjectId' que acabamos de guardar.
                        
                        // Ejemplo de cómo "abrir" un objeto para leerlo:
                        // Polyline parcelaPoly = tr.GetObject(parcelaId, OpenMode.ForRead) as Polyline;
                        // TinSurface terreno = tr.GetObject(terrenoId, OpenMode.ForRead) as TinSurface;
                        
                        // ... lógica pendiente ...
                        ed.WriteMessage("\n(TODO: Implementar lógica de análisis dentro de la Transacción)");


                        // Al final, si todo va bien, confirmamos los cambios (si los hubiera)
                        tr.Commit();
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\n¡Error durante el procesamiento! {ex.Message}");
                        tr.Abort(); // Deshacer cualquier cambio si hay un error
                    }
                } // La transacción se cierra aquí


                ed.WriteMessage("\n--- PROCESO FASE 1 TERMINADO (Lógica principal pendiente) ---");
            }
        }
    } // Cierre del namespace
