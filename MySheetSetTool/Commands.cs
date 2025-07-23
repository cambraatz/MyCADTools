using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.ApplicationServices.LayoutManager;
using Autodesk.AutoCAD.Geometry;
using System.Collections.Generic;
using System.Linq;

// You might need these for file path operations
using System.IO;

namespace MySheetSetTool
{
    public class Commands
    {
        private const bool EnableGeometryDebugLogging = true;

        // Define standard ANSI D sheet size in inches (adjust if your drawing is in mm)
        private const double SheetWidth = 36.0;
        private const double SheetHeight = 24.0;
        private const double ViewportWidth = 30.0; // 22x30 viewport, assuming landscape
        private const double ViewportHeight = 22.0; // 22x30 viewport, assuming landscape

        // Define a path to your title block DWG
        // IMPORTANT: Update this path to where your actual title block file is located.
        // For testing, you can create a simple DWG with some text/border and save it.
        private const string TitleBlockFilePath = "C:\\Users\\camer\\Documents\\Temp\\tempCAD";

        [CommandMethod("GENSHEET")]
        public void GenerateSheetCommand()
        { 
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) { return; }

            Editor ed = doc.Editor;
            Database db = doc.Database;

            ed.WriteMessage("\n--- Generate Paper Space Sheet Tool Started ---");

            // 1) prompt user for sheet set name
            PromptStringOptions psoLayout = new PromptStringOptions("\nEnter the name of the sheet set (ie: 'PlantingPlan01'): ");
            psoLayout.AllowSpaces = true;
            PromptResult prLayout = ed.GetString(psoLayout);
            if (prLayout.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(prLayout.StringResult))
            {
                ed.WriteMessage("\nInvalid sheet set name. Command cancelled.");
                return;
            }
            string layoutName = prLayout.StringResult.Trim();
            
            // check if layout exists
            using (Transaction trCheck = db.TransactionManager.StartTransaction())
            {
                DBDictionary layoutDict = (DBDictionary)trCheck.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                if (layoutDict.Contains(layoutName))
                {
                    ed.WriteMessage($"\nLayout '{layoutName}' already exists, please try a different name. Command cancelled.");
                    trCheck.Abort();
                    return;
                }
            }

            // 2) prompt user to select a scale
            // For now, simple prompt with common scales. We'll use Doubles for scale denominators.
            // e.g., 1:1, 1:2, 1:4, 1:8, 1:16, 1:20, 1:30, 1:40, 1:50, 1:100, etc.
            Dictionary<string,double> scales = new Dictionary<string, double>
            {
                { "1:1", 1.0 },
                { "1:2", 2.0 },
                { "1:4", 4.0 },
                { "1:8", 8.0 },
                { "1:16", 16.0 },
                { "1:20", 20.0 },
                { "1:30", 30.0 },
                { "1:40", 40.0 },
                { "1:50", 50.0 },
                { "1:100", 100.0 }
            };

            string scalePrompt = "\nEnter desired viewport scale (ie: 1:50, 1:10):";
            foreach (var scale in scales)
            { 
                scalePrompt += $"\n    {scale.Key}";
            }
            scalePrompt += "\nScale: ";

            PromptStringOptions psoScale = new PromptStringOptions(scalePrompt);
            psoScale.AllowSpaces = false;
            psoScale.DefaultValue = "1:50"; // Default scale
            psoScale.UseDefaultValue = true;

            PromptResult prScale = ed.GetString(psoScale);
            if (prScale.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(prScale.StringResult) /*|| !scales.ContainsKey(prScale.StringResult)*/)
            {
                ed.WriteMessage("\nInvalid scale selection. Command cancelled.");
                return;
            }

            string enteredScale = prScale.StringResult.Trim();
            double scaleDenominator = 0.0;

            // parse scale string
            if (scales.ContainsKey(enteredScale))
            {
                scaleDenominator = scales[enteredScale];
            }
            else if (enteredScale.Contains(":"))
            {
                // Try to parse custom scale format like "1:50"
                string[] parts = enteredScale.Split(':');
                if (parts.Length == 2 && double.TryParse(parts[0], out double num) && num == 1.0 && double.TryParse(parts[1], out double den))
                {
                    scaleDenominator = den;
                }
                else
                {
                    ed.WriteMessage("\nInvalid scale format. Command cancelled.");
                    return;
                }
            }
            else
            {
                ed.WriteMessage("\nInvalid scale selection. Command cancelled.");
                return;
            }

            if (scaleDenominator <= 0.0)
            {
                ed.WriteMessage("\nInvalid scale denominator. Command cancelled.");
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // 3) create new layout
                // get layout manager and layout dictionary
                LayoutManager lm = LayoutManager.Current;
                DBDictionary layoutDict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForWrite);

                // create and add the new layout
                Layout newLayout = new Layout();
                layoutDict.SetAt(layoutName, newLayout);
                tr.AddNewlyCreatedDBObject(newLayout, true);

                // set curr layout to the newly created one to configure it
                lm.CurrentLayout = layoutName;

                // open the new layouts BlockTableRecord for writing (Paper Space)
                BlockTableRecord layoutBtr = (BlockTableRecord)tr.GetObject(newLayout.BlockTableRecordId, OpenMode.ForWrite);

                // set layout properties (ANSI D - 24x36 inches)
                // adjust as needed for your specific requirements
                newLayout.PlotType = PlotType.Extents;
                newLayout.StandardScale = StdScaleType.StdScale1To1;
                newLayout.PlotRotation = PlotRotation.Degrees090;

                // set paper size (ANSI D - 24x36 inches)
                // For a 24x36, these are usually 36 units wide, 24 units high.
                // We're setting the paper size, not necessarily the printable area margins.
                newLayout.SetPaperSize(new Point2d(SheetWidth,SheetHeight));

                // add the title block xref
                if (File.Exists(TitleBlockFilePath))
                { 
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                    ObjectId xrefBlockDefId;
                    string xrefBlockName = Path.GetFileNameWithoutExtension(TitleBlockFilePath);

                    // check if the block def for the XREF already exists
                    if (!bt.Has(xrefBlockName))
                    {
                        // create a new BlockTableRecord for the XREF
                        using (BlockTableRecord xrefBTR = new BlockTableRecord())
                        {
                            // this creates the XREF ref in the Block Table
                            db.NewXrefGraph(TitleBlockFilePath, xrefBlockName, xrefBTR);
                            bt.Add(xrefBTR);
                            tr.AddNewlyCreatedDBObject(xrefBTR, true);
                            xrefBlockDefId = xrefBTR.ObjectId;
                        }
                    }
                    else
                    {
                        xrefBlockDefId = bt[xrefBlockName];
                    }

                    using (BlockReference xrefRef = new BlockReference(Point3d.Origin, xrefBlockDefId))
                    {
                        layoutBtr.AppendEntity(xrefRef);
                        tr.AddNewlyCreatedDBObject(xrefRef, true);
                        xrefRef.ScaleFactors = new Scale3d(1.0); // insert at 1:1 scale
                        xrefRef.Position = Point3d.Origin; // insert at 0,0,0
                        xrefRef.ColorIndex = 256; // ByLayer
                    }
                    ed.WriteMessage($"\nTitle block '{xrefBlockName}' inserted as XREF.");
                }
                else
                {
                    ed.WriteMessage($"\nWARNING: Title block file not found at '{TitleBlockFilePath}'. Skipping XREF insertion.");
                }

                // Calculate Viewport position and size in Paper Space
                // Viewport is 22x30. Sheet is 24x36.
                // Center the viewport on the layout, assuming it fits inside typical margins.
                // Let's assume a 1" margin all around for a 24x36 sheet.
                // Sheet inner usable area for VP: (36-2*1) x (24-2*1) = 34 x 22
                // A 30x22 VP fits within 34x22.

                // Lower left corner of viewport in paper space
                // (SheetWidth - ViewportWidth) / 2.0
                // (SheetHeight - ViewportHeight) / 2.0
                Point3d viewportCenterPS = new Point3d(SheetWidth / 2.0, SheetHeight / 2.0, 0); // Center of the sheet
                Point3d viewportMinPS = new Point3d(viewportCenterPS.X - ViewportWidth / 2.0, viewportCenterPS.Y - ViewportHeight / 2.0, 0);
                Point3d viewportMaxPS = new Point3d(viewportCenterPS.X + ViewportWidth / 2.0, viewportCenterPS.Y + ViewportHeight / 2.0, 0);

                // create and add the viewport entity
                using (Viewport vp = new Viewport())
                {
                    vp.Width = ViewportWidth;
                    vp.Height = ViewportHeight;
                    vp.CenterPoint = new Point3d(viewportCenterPS.X, viewportCenterPS.Y, 0);

                    // ensure the viewport is ON and is not locked initially
                    vp.On = true;
                    vp.Locked = false;

                    // add to paper space
                    layoutBtr.AppendEntity(vp);
                    tr.AddNewlyCreatedDBObject(vp, true);

                    // set viewport to model space to apply scale
                    ed.SetCurrentView(vp); // Switch editor's current view to this viewport

                    // set the scale of the viewport
                    double modelToPaperRatio = 1.0 / scaleDenominator; // ie: 1/50 for 1:50
                    vp.CustomScale = modelToPaperRatio;

                    // Adjust the viewport's view to center Model Space content
                    // This is a rough centering. A more robust solution would calculate extents.
                    // For now, let's just make sure it's active.
                    // This often requires a "zoom extents" or similar to show content.
                    // We'll add the non-plotting rectangle to guide the user.

                    // IMPORTANT: To see anything in the viewport, you need to set its Viewport.ViewCenter
                    // to a point in Model Space. For now, it will just show whatever is at 0,0.
                    // A better approach involves zooming to the model space extents or a specific area.
                    // For the initial "isolate a section", the user will have to pan/zoom within the new viewport.
                }

                // Create a non-plotting rectangle in Model Space
                // This rectangle's size corresponds to the viewport's *model space* extent at the selected scale.
                // Model Space Rectangle Dimensions: ViewportSize (PS) * ScaleDenominator
                double rectWidthMS = ViewportWidth * scaleDenominator;
                double rectHeightMS = ViewportHeight * scaleDenominator;

                // Create a new Polyline for the rectangle
                using (Polyline rect = new Polyline())
                {
                    rect.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                    rect.AddVertexAt(1, new Point2d(rectWidthMS, 0), 0, 0, 0);
                    rect.AddVertexAt(2, new Point2d(rectWidthMS, rectHeightMS), 0, 0, 0);
                    rect.AddVertexAt(3, new Point2d(0, rectHeightMS), 0, 0, 0);
                    rect.Closed = true;

                    // Set layer to a non-plotting layer (e.g., "Defpoints" or a custom one)
                    // If "Defpoints" doesn't exist, it will create it.
                    string nonPlotLayerName = "VIEWPORT_GUIDE";
                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    if (!lt.Has(nonPlotLayerName))
                    {
                        // Create the layer if it doesn't exist
                        lt.UpgradeOpen(); // Must upgrade to write mode to add a new layer
                        using (LayerTableRecord ltr = new LayerTableRecord())
                        {
                            ltr.Name = nonPlotLayerName;
                            ltr.IsPlottable = false; // Make it non-plotting
                            ltr.ColorIndex = 8; // Grey color, or another distinct color
                            lt.Add(ltr);
                            tr.AddNewlyCreatedDBObject(ltr, true);
                        }
                        lt.DowngradeOpen(); // Downgrade back to read
                    }
                    rect.Layer = nonPlotLayerName;

                    // Add to Model Space
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord msBTR = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    msBTR.AppendEntity(rect);
                    tr.AddNewlyCreatedDBObject(rect, true);
                    ed.WriteMessage($"\nNon-plotting rectangle created in Model Space ({rectWidthMS:F2}x{rectHeightMS:F2}).");
                }

                tr.Commit(); // Commit all changes to the database
                ed.WriteMessage($"\nLayout '{layoutName}' created with {enteredScaleString} viewport and title block.");
            }
        }
    }
}
