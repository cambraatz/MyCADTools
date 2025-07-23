using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using System.Collections.Generic;
using System.Linq;
using MyPlantingTool;

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
            Database db = doc.Database;

            ed.WriteMessage("\n--- Plant Bed Tool Started ---");

            /* 1. Prompt user to select bed hatch entity (accepts hatch and boundaries) */
            // prompt user selection...
            PromptEntityOptions peo = new PromptEntityOptions("\nSelect the bed hatch or its boundary: ");
            peo.AllowNone = false; // mandatory...

            // filter selection to allow only valid geometries *NOT AVAILABLE*...
            peo.SetRejectMessage("\nInvalid selection. Please select a Hatch or a Polyline Boundary.");

            peo.AddAllowedClass(typeof(Hatch), true); // allow hatch selection...
            peo.AddAllowedClass(typeof(Polyline), true); // allow polyline selection...
            peo.AddAllowedClass(typeof(Polyline2d), true); // allow 2D polyline selection...
            peo.AddAllowedClass(typeof(Polyline3d), true); // allow 3D polyline selection...
            peo.AddAllowedClass(typeof(Circle), true); // allow circle selection...

            //peo.SetRejectMessage("\nInvalid selection. Please select a Hatch or a Polyline Boundary.");

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

                        // Define your plant circle radii
                        List<double> plantRadii = new List<double> { 3.0, 5.0, 7.0 }; // Example radii: 3', 5', 7'

                        // Instantiate packing algorithm and run it
                        CirclePacker packer = new CirclePacker(boundaryPoints, plantRadii, GeometryService._defaultTolerance, ed, EnableGeometryDebugLogging);
                        List<PlantCircle> packedCircles = packer.PackCircles();

                        if (packedCircles.Count > 0)
                        {
                            ed.WriteMessage($"\nSuccessfully packed {packedCircles.Count} circles into the extracted boundary.");

                            // Open Model Space for write to add circles
                            BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                            BlockTableRecord btr = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                            foreach (PlantCircle pCircle in packedCircles)
                            {
                                Circle cadCircle = new Circle();
                                cadCircle.Center = new Point3d(pCircle.Center.X, pCircle.Center.Y, 0);
                                cadCircle.Radius = pCircle.Radius;
                                cadCircle.ColorIndex = 1; // Red color for packed circles

                                btr.AppendEntity(cadCircle);
                                tr.AddNewlyCreatedDBObject(cadCircle, true);
                            }
                            // Transaction will be committed at the end of the using block
                        }
                        else
                        {
                            ed.WriteMessage("\nNo circles could be packed into the boundary. It might be too small or have complex geometry.");
                            // If no circles are packed, but a boundary was drawn, we still commit the transaction.
                            // If no boundary was drawn either, we'd abort, but that's handled by the `if (boundaryPoints != null ...)`
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

        /*[CommandMethod("PACKBED")]
        public static void PlantBedPackingCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptEntityOptions peo = new PromptEntityOptions("\nSelect a Hatch object to pack circles into: ");
            peo.SetRejectMessage("\nInvalid selection. Must be a Hatch object.");
            peo.AddAllowedClass(typeof(Hatch), true);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nNo valid Hatch selected. Command aborted.");
                return;
            }
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBObject obj = tr.GetObject(per.ObjectId, OpenMode.ForRead);

                List<Point2d>? boundaryPoints = GeometryService.GetBoundaryPoints(obj, tr, ed, true);
                if (boundaryPoints == null || boundaryPoints.Count < 3)
                {
                    ed.WriteMessage("\nSelected Hatch does not have a valid boundary for packing.");
                    return;
                }

                // define plant circle radius
                List<double> plantRadii = new List<double> { 3.0, 5.0, 7.0 };

                // instantiate packing algorithm and run it
                CirclePacker packer = new CirclePacker(boundaryPoints, plantRadii, GeometryService._defaultTolerance, ed, true);
                List<PlantCircle> packedCircles = packer.PackCircles();

                if (packedCircles.Count > 0)
                {
                    ed.WriteMessage($"\nSuccessfully packed {packedCircles.Count} circles into the selected Hatch boundary.");

                    // Optionally, visualize the packed circles in AutoCAD
                    BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    BlockTableRecord btr = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                    foreach (PlantCircle pCircle in packedCircles)
                    {
                        Circle cadCircle = new Circle();
                        cadCircle.Center = new Point3d(pCircle.Center.X, pCircle.Center.Y, 0);
                        cadCircle.Radius = pCircle.Radius;
                        cadCircle.ColorIndex = 1; // Red color for packed circles

                        btr.AppendEntity(cadCircle);
                        tr.AddNewlyCreatedDBObject(cadCircle, true);
                    }
                    tr.Commit(); // Commit the transaction after adding all circles
                }
                else
                {
                    ed.WriteMessage("\nNo circles could be packed into the selected boundary.");
                    tr.Abort(); // No changes to commit
                }

                
            }
        }*/
    }
}
