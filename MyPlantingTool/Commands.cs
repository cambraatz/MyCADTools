using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using System.Collections.Generic;

namespace MyPlantingTool
{
    public class Commands
    {
        private const bool EnableGeometryDebugLogging = true;

        [CommandMethod("MYPLANTTOOL")]
        public void MyFirstPLantToolCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;

            ed.WriteMessage("\nHello World! This is my first plant tool.");

            using (Transaction tr = doc.TransactionManager.StartTransaction())
            {
                BlockTable bt = tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead) as BlockTable;
                BlockTableRecord btr = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite)! as BlockTableRecord;
            
                Line line = new Line(new Point3d(0, 0, 0), new Point3d(10, 10, 0));
                btr.AppendEntity(line);
                tr.AddNewlyCreatedDBObject(line, true);
            
                tr.Commit();
            }
        }

        [CommandMethod("PLANTBED")]
        public void PlantBedCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;

            ed.WriteMessage("\n--- Plant Bed Tool Started ---");

            /* 1. Prompt user to select bed hatch entity (accepts hatch and boundaries) */
            // prompt user selection...
            PromptEntityOptions peo = new PromptEntityOptions("\nSelect the bed hatch or its boundary: ");
            peo.AllowNone = false; // mandatory...

            // filter selection to allow only valid geometries *NOT AVAILABLE*...
            /*peo.SetFilter(new SelectionFilter(
                new TypedValue[]
                {
                    new TypedValue((int)DxfCode.Start, "LWPOLYLINE"), // lightweight polyline...
                    new TypedValue((int)DxfCode.Start, "POLYLINE"), // old polyline 2d|3d...
                    new TypedValue((int)DxfCode.Start, "HATCH"), // hatch...
                    new TypedValue((int)DxfCode.Start, "CIRCLE") // circle...
                }));*/

            peo.SetRejectMessage("\nInvalid selection. Please select a Hatch or a Polyline Boundary.");

            // set result to selection...
            PromptEntityResult per = ed.GetEntity(peo);

            // ensure valid selection...
            if (per.Status == PromptStatus.OK)
            {
                using (Transaction tr = doc.TransactionManager.StartTransaction())
                {
                    // open selection for reading...
                    DBObject obj = tr.GetObject(per.ObjectId, OpenMode.ForRead);

                    // validate object shape is supported...
                    if (!(obj is Polyline || obj is Polyline2d || obj is Polyline3d || obj is Hatch || obj is Circle))
                    {
                        ed.WriteMessage($"\nSelected object is not a recognized bed boundary type (Hatch, Polyline, Circle). It is a {obj.GetType().Name}. Command aborted.");
                        tr.Commit(); // Commit transaction (no changes made)
                        return;
                    }

                    // call GeometryService to fetch boundary points...
                    List<Point2d>? boundaryPoints = GeometryService.GetBoundaryPoints(obj, tr, ed, EnableGeometryDebugLogging);
                    
                    if (boundaryPoints != null && boundaryPoints.Count > 0)
                    {
                        ed.WriteMessage($"\nSuccessfully extracted {boundaryPoints.Count} boundary points for packing.");

                        // draw a blue polyline over selected boundary...
                        DrawPolyline(doc.Database, tr, boundaryPoints, ed);

                        double plantRadius = 0.0;
                        PromptDoubleOptions pdoRadius = new PromptDoubleOptions("\nEnter plant radius: ");
                        pdoRadius.AllowZero = false;
                        pdoRadius.AllowNegative = false;
                        pdoRadius.AllowNone = false;
                        pdoRadius.DefaultValue = 3.0;
                        pdoRadius.UseDefaultValue = true;
                        PromptDoubleResult pdrRadius = ed.GetDouble(pdoRadius);

                        if (pdrRadius.Status == PromptStatus.OK)
                        {
                            plantRadius = pdrRadius.Value;
                            ed.WriteMessage($"\nPlant radius set to: {plantRadius}");

                            ed.WriteMessage($"\nAuto-planting circle packing algorithm is yet to be implemented.");
                        }
                        else
                        {
                            ed.WriteMessage("\nPlant radius input cancelled. Command aborted.");
                        }
                    }
                    // handle when GetBoundaryPoints returns null or empty list...
                    else
                    {
                        ed.WriteMessage("\nCould not extract a valid boundary from the selected object (it may be open or unsupported internally).");
                    }

                    // commit the transaction...
                    tr.Commit();
                }
            }
            else
            {
                ed.WriteMessage("\n Entity selection cancelled or failed.");
            }

            ed.WriteMessage("\n--- Plant Bed Tool Finished ---");
        }

        private static void DrawPolyline(Database db, Transaction tr, List<Point2d> points, Editor ed)
        {
            if (points == null || points.Count < 2)
            {
                ed.WriteMessage("\nDEBUG: Cannot draw debug polyline - not enough points.");
                return;
            }

            // Open the Model Space Block Table Record for write
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            // Create a new Polyline (Lightweight Polyline)
            Polyline debugPoly = new Polyline();
            debugPoly.ColorIndex = 5; // Blue color for debug

            for (int i = 0; i < points.Count; i++)
            {
                debugPoly.AddVertexAt(i, points[i], 0.0, 0.0, 0.0); // Add 2D vertex, bulge=0 for line segments
            }

            // Ensure the debug polyline is closed if the boundary was meant to be closed
            debugPoly.Closed = true;

            // Add the polyline to Model Space
            btr.AppendEntity(debugPoly);
            tr.AddNewlyCreatedDBObject(debugPoly, true);

            ed.WriteMessage($"\nDEBUG: Drew a blue debug polyline with {points.Count} vertices representing the extracted boundary.");
        }
    }
}
