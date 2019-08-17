using System;
using System.Collections.Generic;
using System.Numerics;
using static MinesweeperControl.MinesweeperGame;

namespace MinesweeperSolver {
    public class ProbabilityEngine {

        private readonly int[][] SMALL_COMBINATIONS = new int[][] { new int[] { 1 }, new int[] { 1, 1 }, new int[] { 1, 2, 1 }, new int[] { 1, 3, 3, 1 }, new int[] { 1, 4, 6, 4, 1 }, new int[] { 1, 5, 10, 10, 5, 1 }, new int[] { 1, 6, 15, 20, 15, 6, 1 }, new int[] { 1, 7, 21, 35, 35, 21, 7, 1 }, new int[] { 1, 8, 28, 56, 70, 56, 28, 8, 1 } };

        private readonly List<SolverTile> witnessed;
        private readonly List<BoxWitness> prunedWitnesses = new List<BoxWitness>();  // a subset of allWitnesses with equivalent witnesses removed
        private readonly List<Box> boxes = new List<Box>();
        private readonly List<BoxWitness> boxWitnesses = new List<BoxWitness>();
        private bool[] mask;

        private readonly Dictionary<SolverTile, Box> boxLookup = new Dictionary<SolverTile, Box>();  // a lookup which finds which box a tile belongs to (an efficiency enhancement)
        private readonly List<DeadCandidate> deadCandidates = new List<DeadCandidate>();

        private List<ProbabilityLine> workingProbs = new List<ProbabilityLine>();
        private readonly List<ProbabilityLine> heldProbs = new List<ProbabilityLine>();
        private readonly List<EdgeStore> edgeStore = new List<EdgeStore>(); // stores independent edge analysis until we know we have to merge them


        // details about independent witnesses
        private List<BoxWitness> independentWitnesses = new List<BoxWitness>();
        private List<BoxWitness> dependentWitnesses = new List<BoxWitness>();
        private int independentMines;
        private BigInteger independentIterations = 1;
        private int remainingSquares = 0;

        // local clears which means we can stop the probability engine early
        private readonly List<SolverTile> localClears = new List<SolverTile>();

        private int minesLeft;
        private int tilesLeft;
        private int tilesOffEdge;
        private int minTotalMines;
        private int maxTotalMines;
        private int recursions;


        private double bestProbability = 0;
        private double offEdgeProbability = 0;
        private BigInteger finalSolutionCount = 0;
        private BigInteger solutionCountMultiplier = 1;

        private readonly SolverInfo information;

        public ProbabilityEngine(SolverInfo information, List<SolverTile> allWitnesses, List<SolverTile> allWitnessed, int squaresLeft, int minesLeft) {

            this.information = information;

		    this.witnessed = allWitnessed;

            // constraints in the game
            this.minesLeft = minesLeft;   
            this.tilesLeft = squaresLeft;
            this.tilesOffEdge = squaresLeft - allWitnessed.Count;   // squares left off the edge and unrevealed
            this.minTotalMines = minesLeft - this.tilesOffEdge;     //we can't use so few mines that we can't fit the remainder elsewhere on the board
            this.maxTotalMines = minesLeft;

            information.Write("Tiles off edge " + tilesOffEdge);

            //this.boxProb = [];  // the probabilities end up here

             // generate a BoxWitness for each witness tile and also create a list of pruned witnesses for the brute force search
            int pruned = 0;
            foreach (SolverTile wit in allWitnesses) {

                BoxWitness boxWit = new BoxWitness(information, wit);

                // if the witness is a duplicate then don't store it
                bool duplicate = false;
                foreach (BoxWitness w in this.boxWitnesses) {

                    if (w.Equivalent(boxWit)) {
                        //if (boardState.getWitnessValue(w) - boardState.countAdjacentConfirmedFlags(w) != boardState.getWitnessValue(wit) - boardState.countAdjacentConfirmedFlags(wit)) {
                        //    boardState.display(w.display() + " and " + wit.display() + " share unrevealed squares but have different mine totals!");
                        //    validWeb = false;
                        //}
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate) {
                    this.prunedWitnesses.Add(boxWit);
                 } else {
                    pruned++;
                }
                this.boxWitnesses.Add(boxWit);  // all witnesses are needed for the probability engine
            }
            information.Write("Pruned " + pruned + " witnesses as duplicates");
            information.Write("There are " + this.boxWitnesses.Count + " Box witnesses");

            // allocate each of the witnessed squares to a box
            int uid = 0;
            foreach (SolverTile tile in this.witnessed) {

                // for each adjacent tile see if it is a witness
                int count = 0;
                foreach (SolverTile adjTile in information.GetAdjacentTiles(tile)) {
                    if (information.GetWitnesses().Contains(adjTile)) {
                        count++;
                    }
                }

                // count how many adjacent witnesses the tile has
                //foreach (SolverTile tile1 in allWitnesses) {
                //    if (tile.IsAdjacent(tile1)) {
                //       count++;
                //    }
                //}

                // see if the witnessed tile fits any existing boxes
                bool found = false;
                foreach (Box box in this.boxes) {

                    if (box.Fits(tile, count)) {
                        box.Add(tile);
                        boxLookup.Add(tile, box);   // add this to the lookup
                        found = true;
                        break;
                    }

                }

                // if not found create a new box and store it
                if (!found) {
                    Box box = new Box(this.boxWitnesses, tile, uid++);
                    this.boxes.Add(box);
                    boxLookup.Add(tile, box);   // add this to the lookup
                }

            }   

            // calculate the min and max mines for each box 
            foreach (Box box in this.boxes) {
                box.Calculate(this.minesLeft);
                //console.log("Box " + box.tiles[0].asText() + " has min mines = " + box.minMines + " and max mines = " + box.maxMines);
            }

            // Report how many boxes each witness is adjacent to 
            foreach (BoxWitness boxWit in this.boxWitnesses) {
                information.Write("Witness " + boxWit.GetTile().AsText() + " is adjacent to " + boxWit.GetBoxes().Count + " boxes and has " + boxWit.GetMinesToFind() + " mines to find");
            }

        }


        // calculate a probability for each un-revealed tile on the board
        public void Process() {

            this.mask = new bool[this.boxes.Count];
            // create an array showing which boxes have been procesed this iteration - none have to start with
            //for (var i = 0; i < this.boxes.length; i++) {
            //    this.mask.push(false);
            //}

            // look for places which could be dead
            GetCandidateDeadLocations();

            // create an initial solution of no mines anywhere 
            ProbabilityLine held = new ProbabilityLine(this.boxes.Count);
            held.SetSolutionCount(1);
            this.heldProbs.Add(held);

            // add an empty probability line to get us started
            this.workingProbs.Add(new ProbabilityLine(this.boxes.Count));

            NextWitness nextWitness = FindFirstWitness();

            while (nextWitness != null) {

                //console.log("Probability engine processing witness " + nextWitness.boxWitness.tile.asText());

                // mark the new boxes as processed - which they will be soon
                foreach (Box box in nextWitness.GetNewBoxes()) {
                    this.mask[box.GetUID()] = true;
                }

                this.workingProbs = MergeProbabilities(nextWitness);

                //if (this.workingProbs.length > 10) {
                //    console.log("Items in the working array = " + this.workingProbs.length);
                //}

                nextWitness = FindNextWitness(nextWitness);

            }

            // if we don't have any local clears then do a full probability determination
            if (this.localClears.Count  == 0) {
                CalculateBoxProbabilities();
            }


        }


        // take the next witness details and merge them into the currently held details
        private List<ProbabilityLine> MergeProbabilities(NextWitness nw) {

            List<ProbabilityLine> newProbs = new List<ProbabilityLine>();

            foreach (ProbabilityLine pl in this.workingProbs) {

                int missingMines = nw.GetBoxWitness().GetMinesToFind() - (int) CountPlacedMines(pl, nw);

                if (missingMines < 0) {
                    //console.log("Missing mines < 0 ==> ignoring line");
                    // too many mines placed around this witness previously, so this probability can't be valid
                } else if (missingMines == 0) {
                    //console.log("Missing mines = 0 ==> keeping line as is");
                    newProbs.Add(pl);   // witness already exactly satisfied, so nothing to do
                } else if (nw.GetNewBoxes().Count == 0) {
                    //console.log("new boxes = 0 ==> ignoring line since nowhere for mines to go");
                    // nowhere to put the new mines, so this probability can't be valid
                } else {

                    List<ProbabilityLine> result = DistributeMissingMines(pl, nw, missingMines, 0);
                    newProbs.AddRange(result);

                    //for (var j = 0; j < result.length; j++) {
                    //   newProbs.push(result[j]);
                    //}

                }

            }

            //if (newProbs.length == 0) {
            //     console.log("Returning no lines from merge probability !!");
            //}

            return newProbs;

        }

        // counts the number of mines already placed
        private BigInteger CountPlacedMines(ProbabilityLine pl, NextWitness nw) {

            BigInteger result = 0;

            foreach (Box b in nw.GetOldBoxes()) {

                result = result + pl.GetMineBoxCount(b.GetUID());
            }

            return result;
        }

        // this is used to recursively place the missing Mines into the available boxes for the probability line
        private List<ProbabilityLine> DistributeMissingMines(ProbabilityLine pl, NextWitness nw, int missingMines, int index) {

            //console.log("Distributing " + missingMines + " missing mines to box " + nw.newBoxes[index].uid);

            this.recursions++;
            if (this.recursions % 100 == 0) {
                information.Write("Probability Engine recursision = " + recursions);
            }

            List<ProbabilityLine> result = new List<ProbabilityLine>();

            // if there is only one box left to put the missing mines we have reach the end of this branch of recursion
            if (nw.GetNewBoxes().Count - index == 1) {
                // if there are too many for this box then the probability can't be valid
                if (nw.GetNewBoxes()[index].GetMaxMines() < missingMines) {
                    //console.log("Abandon (1)");
                    return result;
                }
                // if there are too few for this box then the probability can't be valid
                if (nw.GetNewBoxes()[index].GetMinMines() > missingMines) {
                    //console.log("Abandon (2)");
                    return result;
                }
                // if there are too many for this game then the probability can't be valid
                if (pl.GetMineCount() + missingMines > this.maxTotalMines) {
                    //console.log("Abandon (3)");
                    return result;
                }

                // otherwise place the mines in the probability line
                pl.SetMineBoxCount(nw.GetNewBoxes()[index].GetUID(), new BigInteger(missingMines));

                pl.SetMineCount(pl.GetMineCount() + missingMines);
                result.Add(pl);
                //console.log("Distribute missing mines line after " + pl.mineBoxCount);
                return result;
            }


            // this is the recursion
            int maxToPlace = Math.Min(nw.GetNewBoxes()[index].GetMaxMines(), missingMines);

            for (int i = nw.GetNewBoxes()[index].GetMinMines(); i <= maxToPlace; i++) {

                ProbabilityLine npl = ExtendProbabilityLine(pl, nw.GetNewBoxes()[index], i);

                List<ProbabilityLine> r1 = DistributeMissingMines(npl, nw, missingMines - i, index + 1);
                result.AddRange(r1);

                //for (var j = 0; j < r1.length; j++) {
                //    result.push(r1[j]);
                //}

                //result.push(distributeMissingMines(npl, nw, missingMines - i, index + 1));
            }

            return result;

        }

        // create a new probability line by taking the old and adding the mines to the new Box
        private ProbabilityLine ExtendProbabilityLine(ProbabilityLine pl, Box newBox, int mines) {

            //console.log("Extended probability line: Adding " + mines + " mines to box " + newBox.uid);
            //console.log("Extended probability line before" + pl.mineBoxCount);

            ProbabilityLine result = new ProbabilityLine(this.boxes.Count);

            result.SetMineCount(pl.GetMineCount() + mines);
            //result.solutionCount = pl.solutionCount;

            // copy the probability array

            //for (var i = 0; i < pl.mineBoxCount.length; i++) {
            //    result.mineBoxCount[i] = pl.mineBoxCount[i];
            //}

            result.CopyMineBoxCount(pl);

            result.SetMineBoxCount(newBox.GetUID(), new BigInteger(mines));

            //console.log("Extended probability line after " + result.mineBoxCount);

            return result;
        }

        // here we store the information we have found about this independent edge for later use
        private void StoreProbabilities() {

            List<ProbabilityLine> crunched = CrunchByMineCount(this.workingProbs);

            //Console.WriteLine("Edge has " + crunched.Count + " probability lines after consolidation");

            edgeStore.Add(new EdgeStore(crunched));  // store the existing data 

            workingProbs.Clear();  // and get a new list for the next independent edge

            this.workingProbs.Add(new ProbabilityLine(this.boxes.Count));  // add a new starter probability line

            // try and determine if every tile on this edge is dead
            if (crunched.Count == 1) {   // if the edge has the same number of mines for every possible solution
 
                bool edgeDead = true;
                for (int i = 0; i < this.mask.Length; i++) {
                    if (this.mask[i] && !boxes[i].IsDead()) {   // if any of the boxes on this edge is not dead then the edge isn't dead
                        edgeDead = false;
                    }
                }

                if (edgeDead) {
                    information.Write("Dead edge found");
                    information.ExcludeMines(crunched[0].GetMineCount());

                    for (int i = 0; i < this.mask.Length; i++) {
                        if (this.mask[i]) {
                            foreach (BoxWitness bw in boxes[i].GetBoxWitnesses()) {   // exclude the witnesses
                                information.ExcludeWitness(bw.GetTile());

                            }

                            foreach (SolverTile tile in boxes[i].GetTiles()) {  // exclude the tiles
                                information.ExcludeTile(tile);
                            }
                         }
                    }

                    //information.ExcludedTilesCount(sizeExcluded);
                    information.Write("Excluding " + crunched[0].GetMineCount() + " mines");
                }

            }

            int inEdge = 0;
            // reset the mask indicating that no boxes have been processed 
            for (int i = 0; i < this.mask.Length; i++) {
                if (this.mask[i]) {
                    inEdge++;
                }
                this.mask[i] = false;
            }
            //if (inEdge == 0) {
            //    Console.WriteLine("Edge has no witnesses processed !!");
            //}


        }

        // this combines newly generated probabilities with ones we have already stored from other independent sets of witnesses
        private void CombineProbabilities() {

            List<ProbabilityLine> result = new List<ProbabilityLine>();

            if (this.workingProbs.Count == 0) {
                information.Write("working probabilites list is empty!!");
                return;
            }

            // see if we can find a common divisor
            BigInteger hcd = workingProbs[0].GetSolutionCount();
            foreach (ProbabilityLine pl in workingProbs) {
                hcd = BigInteger.GreatestCommonDivisor(hcd, pl.GetSolutionCount());
            }
            foreach (ProbabilityLine pl in heldProbs) {
                hcd = BigInteger.GreatestCommonDivisor(hcd, pl.GetSolutionCount());
            }
            information.Write("Greatest Common Divisor is " + hcd);

            solutionCountMultiplier = solutionCountMultiplier * hcd;

            foreach (ProbabilityLine pl in workingProbs) {

                BigInteger plSolCount = pl.GetSolutionCount() / hcd;

                foreach (ProbabilityLine epl in heldProbs) {

                    if (pl.GetMineCount() + epl.GetMineCount() <= this.maxTotalMines) {

                        ProbabilityLine newpl = new ProbabilityLine(this.boxes.Count);

                        newpl.SetMineCount(pl.GetMineCount() + epl.GetMineCount());

                        BigInteger eplSolCount = epl.GetSolutionCount() / hcd;

                        newpl.SetSolutionCount(pl.GetSolutionCount() * eplSolCount);

                        for (int k = 0; k < this.boxes.Count; k++) {

                            BigInteger w1 = pl.GetMineBoxCount(k) * eplSolCount;
                            BigInteger w2 = epl.GetMineBoxCount(k) * plSolCount;
                            newpl.SetMineBoxCount(k, w1 + w2);

                            //npl.hashCount[i] = epl.hashCount[i].add(pl.hashCount[i]);

                        }

                        /*
                        newpl.SetSolutionCount(pl.GetSolutionCount() * epl.GetSolutionCount());

                        for (int k = 0; k < this.boxes.Count; k++) {

                            BigInteger w1 = pl.GetMineBoxCount(k) * epl.GetSolutionCount();
                            BigInteger w2 = epl.GetMineBoxCount(k) * pl.GetSolutionCount();
                            newpl.SetMineBoxCount(k, w1 + w2);

                            //npl.hashCount[i] = epl.hashCount[i].add(pl.hashCount[i]);

                        }
                        */
                        result.Add(newpl);

                    }
                }
            }

            //Console.WriteLine("Solution multiplier is " + solutionCountMultiplier);

            this.heldProbs.Clear();

            // if result is empty this is an impossible position
            if (result.Count == 0) {
                return;
            }

            if (result.Count == 1) {
                heldProbs.AddRange(result);
                return;
            }

            // sort into mine order 
            result.Sort();

            // and combine them into a single probability line for each mine count
            int mc = result[0].GetMineCount();
            ProbabilityLine npl = new ProbabilityLine(this.boxes.Count);
            npl.SetMineCount(mc);

            int startMC = mc;

            foreach (ProbabilityLine pl in result) {

                if (pl.GetMineCount() != mc) {

                    if (pl.GetMineCount() > startMC + 9) {
                        break;
                    }

                    this.heldProbs.Add(npl);
                    mc = pl.GetMineCount();
                    npl = new ProbabilityLine(this.boxes.Count);
                    npl.SetMineCount(mc);
                }
                npl.SetSolutionCount(npl.GetSolutionCount() + pl.GetSolutionCount());

                for (int j = 0; j < this.boxes.Count; j++) {
                    npl.SetMineBoxCount(j, npl.GetMineBoxCount(j) + pl.GetMineBoxCount(j));

                    //npl.hashCount[i] = npl.hashCount[i].add(pl.hashCount[i]);
                }
            }

            this.heldProbs.Add(npl);

            //if (this.heldProbs.length > 10) {
            //    console.log("Items in the held array = " + this.heldProbs.length);
            //}

            /*
            for (Box b: boxes) {
                System.out.print(b.getSquares().size() + " ");
            }
            System.out.println("");
            for (ProbabilityLine pl: heldProbs) {
                System.out.print("Mines = " + pl.mineCount + " solutions = " + pl.solutionCount + " boxes: ");
                for (int i=0; i < pl.mineBoxCount.length; i++) {
                    System.out.print(" " + pl.mineBoxCount[i]);
                }
                System.out.println("");
            }
            */


        }

        private List<ProbabilityLine> CrunchByMineCount(List<ProbabilityLine> target) {

            //if (target.Count == 0) {
            //    return target;
            //}

            // sort the solutions by number of mines
            target.Sort();

            List<ProbabilityLine> result = new List<ProbabilityLine>();

            int mc = target[0].GetMineCount();
            ProbabilityLine npl = new ProbabilityLine(this.boxes.Count);
            npl.SetMineCount(mc);

            foreach (ProbabilityLine pl in target) {

                if (pl.GetMineCount() != mc) {
                    result.Add(npl);
                    mc = pl.GetMineCount();
                    npl = new ProbabilityLine(this.boxes.Count);
                    npl.SetMineCount(mc);
                }
                MergeLineProbabilities(npl, pl);
            }

            //if (npl.GetMineCount() >= minTotalMines) {
                result.Add(npl);
            //}	
            //information.Write("Probability line has " + npl.GetMineCount() + " mines");
            //SolverMain.Write(target.Count + " Probability Lines compressed to " + result.Count); 

            return result;

        }

        // calculate how many ways this solution can be generated and roll them into one
        private void MergeLineProbabilities(ProbabilityLine npl, ProbabilityLine pl) {

            BigInteger solutions = 1;
            for (int i = 0; i < this.boxes.Count; i++) {
                solutions = solutions * (BigInteger) SMALL_COMBINATIONS[this.boxes[i].GetTiles().Count][(int) pl.GetMineBoxCount(i)];
            }

            npl.SetSolutionCount(npl.GetSolutionCount() + solutions);

            for (int i = 0; i < this.boxes.Count; i++) {
                if (this.mask[i]) {  // if this box has been involved in this solution - if we don't do this the hash gets corrupted by boxes = 0 mines because they weren't part of this edge
                    npl.SetMineBoxCount(i, npl.GetMineBoxCount(i) + pl.GetMineBoxCount(i) * solutions);

                    //if (pl.mineBoxCount[i].signum() == 0) {
                    //    npl.hashCount[i] = npl.hashCount[i].subtract(pl.hash.multiply(BigInteger.valueOf(boxes.get(i).getSquares().size())));   // treat no mines as -1 rather than zero
                    //} else {
                    //    npl.hashCount[i] = npl.hashCount[i].add(pl.mineBoxCount[i].multiply(pl.hash));
                    //}
                }

            }

        }

        // return any witness which hasn't been processed
        private NextWitness FindFirstWitness() {

            BoxWitness excluded = null;
            foreach (BoxWitness boxWit in this.boxWitnesses) {
                 if (!boxWit.IsProcessed() && !boxWit.GetTile().IsExcluded()) {
                    return new NextWitness(boxWit);
                 } else if (!boxWit.IsProcessed()) {
                    excluded = boxWit;
                 }
            }
            if (excluded != null) {
                return new NextWitness(excluded);
            }

            return null;
        }

        // look for the next witness to process
        private NextWitness FindNextWitness(NextWitness prevWitness) {

            // flag the last set of details as processed
            prevWitness.GetBoxWitness().SetProcessed(true);

            foreach (Box newBox in prevWitness.GetNewBoxes()) {
                newBox.SetProcessed(true);
            }

            int bestTodo = 99999;
            BoxWitness bestWitness = null;

            // and find a witness which is on the boundary of what has already been processed
            foreach (Box b in this.boxes) {
                if (b.IsProcessed()) {
                    foreach (BoxWitness w in b.GetBoxWitnesses()) {
                        if (!w.IsProcessed()) {
                            int todo = 0;
                            foreach (Box b1 in w.GetBoxes()) {
                                if (!b1.IsProcessed()) {
                                    todo++;
                                }
                            }
                            if (todo == 0) {    // prioritise the witnesses which have the least boxes left to process
                                return new NextWitness(w);
                            } else if (todo < bestTodo) {
                                bestTodo = todo;
                                bestWitness = w;
                            }
                        }
                    }
                }
            }

            if (bestWitness != null) {
                return new NextWitness(bestWitness);
            } else {
                information.Write("Ending independent edge");
            }

            //Console.WriteLine("Edge has " + this.workingProbs.Count + " probability lines");
            // if we are down here then there is no witness which is on the boundary, so we have processed a complete set of independent witnesses 

            //information.Write("Considering " + this.workingProbs.Count + " probability lines");
            //foreach (ProbabilityLine wp in this.workingProbs) {
            //    information.Write("Probability line has " + wp.GetMineCount() + "mines and " + wp.GetSolutionCount() + " solutions");
            //}

            // see if any of the tiles on this independent edge are dead
            CheckCandidateDeadLocations();

            // look to see if this sub-section of the edge has any certain clears
            for (int i = 0; i < this.mask.Length; i++) {
                if (this.mask[i]) {

                    bool isClear = true;
                    foreach (ProbabilityLine wp in this.workingProbs) {
                        if (wp.GetMineBoxCount(i) != BigInteger.Zero) {
                            //information.Write("Box " + i + " has non-zero mine box count " + wp.GetMineBoxCount(i));
                            isClear = false;
                            break;
                        }
                    }
                    if (isClear) {
                        // if the box is locally clear then store the tiles in it
                        foreach (SolverTile tile in this.boxes[i].GetTiles()) {

                            information.Write(tile.AsText() + " has been determined locally to be clear");
                            //tile.setProbability(1);
                            this.localClears.Add(tile);
                        }
                    }

                    /*
                    bool isFlag = true;
                    foreach (ProbabilityLine wp in this.workingProbs) {
                       if (wp.GetMineBoxCount(i) != wp.GetSolutionCount() * this.boxes[i].GetTiles().Count) {
                            isFlag = false;
                            break;
                        }
                    }
                    if (isFlag) {
                        foreach (SolverTile tile in this.boxes[i].GetTiles()) {
                            SolverMain.Write(tile.AsText() + " has been determined locally to be a mine");
                            //tile.setProbability(1);
                            //this.localClears.Add(tile);
                        }
                    }
                    */
                }

            }

            // if we have found some local clears then stop and use these
            if (this.localClears.Count > 0) {
                information.Write(this.localClears.Count + " Local clears have been found");
                return null;
            }

            //independentGroups++;

            // since we have calculated all the mines in an independent set of witnesses we can crunch them down and store them for later

            // store the details for this edge
            StoreProbabilities();


            // get an unprocessed witness
            NextWitness nw = FindFirstWitness();
            if (nw != null) {
                information.Write("Starting a new independent edge");
            }

             // only crunch it down for non-trivial probability lines unless it is the last set - this is an efficiency decision
             if (nw == null) {

                edgeStore.Sort(EdgeStore.SortByLineCount);

                //long start = DateTime.Now.Ticks;
                foreach (EdgeStore edgeDetails in edgeStore) {
                    workingProbs = edgeDetails.data;
                    CombineProbabilities();
                }
                //Console.WriteLine("Combined all edges in " + (DateTime.Now.Ticks - start) + " ticks");

            }

            // return the next witness to process
            return nw;

        }


        // check the candidate dead locations with the information we have
        private void CheckCandidateDeadLocations() {

            bool completeScan;
            if (this.tilesOffEdge == 0) {
                completeScan = true;   // this indicates that every box has been considered in one sweep (only 1 independent edge)
                for (int i = 0; i < this.mask.Length; i++) {
                    if (!this.mask[i]) {
                        completeScan = false;
                        break;
                    }
                }
                if (completeScan) {
                    information.Write("This is a complete scan");
                } else {
                    information.Write("This is not a complete scan");
                }
            } else {
                completeScan = false;
                information.Write("This is not a complete scan because there are squares off the edge");
            }


            foreach (DeadCandidate dc in this.deadCandidates) {

                if (dc.IsAlive()) {  // if this location isn't dead then no need to check any more
                    continue;
                }

                // only do the check if all the boxes have been analysed in this probability iteration
                int boxesInScope = 0;
                foreach (Box b in dc.GetGoodBoxes()) {
                    if (this.mask[b.GetUID()]) {
                        boxesInScope++;
                    }
                }
                foreach (Box b in dc.GetBadBoxes()) {
                    if (this.mask[b.GetUID()]) {
                        boxesInScope++;
                    }
                }
                if (boxesInScope == 0) {
                    continue;
                } else if (boxesInScope != dc.GetGoodBoxes().Count + dc.GetBadBoxes().Count) {
                    information.Write("Location " + dc.GetCandidate().AsText() + " has some boxes in scope and some out of scope so assumed alive");
                    dc.SetAlive();
                    continue;
                }


                bool okay = true;
                int mineCount = 0;
                foreach (ProbabilityLine pl in this.workingProbs) {

                    if (completeScan && pl.GetMineCount() != this.minesLeft) {
                        continue;
                    }

                    // ignore probability lines where the candidate is a mine
                    if (pl.GetMineBoxCount(dc.GetMyBox().GetUID()) == dc.GetMyBox().GetTiles().Count) {
                        mineCount++;
                        continue;
                    }

                    // all the bad boxes must be zero
                    foreach (Box b in dc.GetBadBoxes()) {

                        if (pl.GetMineBoxCount(b.GetUID()) != 0) {
                            information.Write("Location " + dc.GetCandidate().AsText() + " is not dead because a bad box is non-zero");
                            okay = false;
                            break;
                        }
                    }

                    // if we aren't okay then no need to continue
                    if (!okay) {
                        break;
                    }

                    BigInteger tally = 0;
                    // the number of mines in the good boxes must always be the same
                    foreach (Box b in dc.GetGoodBoxes()) {
                        tally = tally + pl.GetMineBoxCount(b.GetUID());
                    }
                    //boardState.display("Location " + dc.candidate.display() + " has mine tally " + tally);
                    if (!dc.CheckTotalOkay(tally)) {
                        information.Write("Location " + dc.GetCandidate().AsText() + " is not dead because the sum of mines in good boxes is not constant. Was "
                            + dc.GetTotal() + " now " + tally + ". Mines in probability line " + pl.GetMineCount());
                        okay = false;
                        break;
                    }
                }

                // if a check failed or every this tile is a mine for every solution then it is alive
                if (!okay || mineCount == this.workingProbs.Count) {
                    dc.SetAlive();
                } else {
                    information.Write(dc.GetCandidate().AsText() + " is dead");
                    //this.deadTiles.Add(dc.GetCandidate());
                    information.SetTileToDead(dc.GetCandidate());   // set the tile as dead. A dead tile can never be resurrected.
                }

            }

        }


        // find a list of locations which could be dead
        private void GetCandidateDeadLocations() {

            // for each square on the edge
            foreach (SolverTile tile in this.witnessed) {

                // if we have already decided this tile is dead not much more to do
                if (tile.IsDead()) {
                    continue;
                }

                List<Box> adjBoxes = GetAdjacentBoxes(tile);

                if (adjBoxes == null) {  // this happens when the square isn't fully surrounded by boxes
                    continue;
                }

                DeadCandidate dc = new DeadCandidate(tile, GetBox(tile));

                foreach (Box box in adjBoxes) {

                    bool good = true;
                    foreach (SolverTile square in box.GetTiles()) {

                        if (!square.IsAdjacent(tile) && !square.IsEqual(tile)) {
                            good = false;
                            break;
                        }
                    }
                    if (good) {
                        dc.AddToGoodBoxes(box);
                    } else {
                        dc.AddToBadBoxes(box);
                    }

                }

                this.deadCandidates.Add(dc);

            }

            foreach (DeadCandidate dc in this.deadCandidates) {
                information.Write(dc.GetCandidate().AsText() + " is candidate dead with " + dc.GetGoodBoxes().Count + " good boxes and " + dc.GetBadBoxes().Count + " bad boxes");
            }

        }

        // get the box containing this tile
        private Box GetBox(SolverTile tile) {

            foreach (Box b in this.boxes) {
                if (b.Contains(tile)) {
                    return b;
                }
            }

            return null;
        }

        // return all the boxes adjacent to this tile
        private List<Box> GetAdjacentBoxes(SolverTile loc) {

            List<Box> result = new List<Box>();

            // get each adjacent location
            foreach (SolverTile adjLoc in information.GetAdjacentTiles(loc)) {

                // we only want adjacent tile which are hidden (i.e. not clear and not mines)
                if (!adjLoc.IsHidden()) {
                    continue;
                }

                // lookup the box this adjacent tile is in
                boxLookup.TryGetValue(adjLoc, out Box box);   

                // if a box can't be found for the adjacent tile then the candidate can't be dead
                if (box == null) {
                    return null;
                }

                // make sure we only include the box once
                bool alreadyIncluded = false;
                foreach (Box box1 in result) {

                    if (box.GetUID() == box1.GetUID()) {
                        alreadyIncluded = true;
                        break;
                    }
                }
                // if not add it
                if (!alreadyIncluded) {
                    result.Add(box);
                }

            }

            return result;

        }

        // determine a set of independent witnesses which can be used to brute force the solution space more efficiently then a basic 'pick r from n' 
        private void GenerateIndependentWitnesses() {

            this.remainingSquares = this.witnessed.Count;

            // find a set of witnesses which don't share any squares (there can be many of these, but we just want one to use with the brute force iterator)
            foreach (BoxWitness w in this.prunedWitnesses) {

                //console.log("Checking witness " + w.tile.asText() + " for independence");

                bool okay = true;
                foreach (BoxWitness iw in this.independentWitnesses) {

                    if (w.Overlap(iw)) {
                        okay = false;
                        break;
                    }
                }

                // split the witnesses into dependent ones and independent ones 
                if (okay) {
                    //console.log("true");
                    this.remainingSquares = this.remainingSquares - w.GetAdjacentTiles().Count;
                    this.independentIterations = this.independentIterations * SolverMain.Calculate(w.GetMinesToFind(), w.GetAdjacentTiles().Count);
                    this.independentMines = this.independentMines + w.GetMinesToFind();
                    this.independentWitnesses.Add(w);
                } else {
                    this.dependentWitnesses.Add(w);
                }
            }

            information.Write("Calculated " + this.independentWitnesses.Count + " independent witnesses");

        }

        // here we expand the localised solution to one across the whole board and
        // sum them together to create a definitive probability for each box
        private void CalculateBoxProbabilities() {

            //long start = DateTime.Now.Ticks;

            information.Write("Solution count multiplier is " + this.solutionCountMultiplier);

            BigInteger[] tally = new BigInteger[this.boxes.Count];

            // total game tally
            BigInteger totalTally = 0;

            // outside a box tally
            BigInteger outsideTally = 0;

            //console.log("There are " + this.heldProbs.length + " different mine counts on the edge");

            int lowesti = 0;
            int linesProcessed = 0;
            // calculate how many mines 
            foreach (ProbabilityLine pl in this.heldProbs) {

                //console.log("Mine count is " + pl.mineCount + " with solution count " + pl.solutionCount + " mineBoxCount = " + pl.mineBoxCount);

                if (pl.GetMineCount() >= this.minTotalMines) {    // if the mine count for this solution is less than the minimum it can't be valid

                    linesProcessed++;

                    //console.log("Mines left " + this.minesLeft + " mines on PL " + pl.mineCount + " squares left = " + this.squaresLeft);
                    BigInteger mult = SolverMain.Calculate(this.minesLeft - pl.GetMineCount(), this.tilesOffEdge);  //# of ways the rest of the board can be formed

                    information.Write("Mines in solution " + pl.GetMineCount() + " solution count " + pl.GetSolutionCount() + " multiplier " + mult);

                    outsideTally = outsideTally + mult * new BigInteger(this.minesLeft - pl.GetMineCount()) * (pl.GetSolutionCount());

                    // this is all the possible ways the mines can be placed across the whole game
                    totalTally = totalTally + mult * (pl.GetSolutionCount());

                    for (int j = 0; j < tally.Length; j++) {
                        //tally[j] = tally[j] + mult * pl.GetMineBoxCount(j);
                        tally[j] = tally[j] + (mult * pl.GetMineBoxCount(j)) / new BigInteger(this.boxes[j].GetTiles().Count);
                        //hashTally[i] = hashTally[i].add(pl.hashCount[i]);
                    }

                    if (tally.Length > 0) {
                        lowesti = 0;
                        for (int j = 1; j < tally.Length; j++) {
                            if (tally[j] < tally[lowesti]) {
                                lowesti = j;
                            }
                        }
                        //Console.WriteLine("Lowest mine count after first " + linesProcessed + " probability lines is at " + boxes[lowesti].GetTiles()[0].AsText());
                    }

                }

            }

            // for each box calcaulate a probability
            for (int i = 0; i < this.boxes.Count; i++) {

                if (totalTally != 0) {
                    if (tally[i] == totalTally) {  // a mine
                        information.Write("Box " + i + " contains all mines");
                        foreach (SolverTile tile in boxes[i].GetTiles()) {
                            information.MineFound(tile);
                        }
                        boxes[i].SetProbability(0);
                        //this.boxProb[i] = 0;
                        //for (Square squ: boxes.get(i).getSquares()) {  // add the squares in the box to the list of mines
                        //   mines.add(squ);
                        //}
                    } else {
                         //this.boxProb[i] = 1 - Combination.DivideBigIntegerToDouble(tally[i], totalTally, 6);
                        boxes[i].SetProbability(1 - Combination.DivideBigIntegerToDouble(tally[i], totalTally, 6));
                    }

                } else {
                    information.Write("total Tally is zero!");
                    boxes[i].SetProbability(0);
                    //this.boxProb[i] = 0;
                }

                //console.log("Box " + i + " has probabality " + this.boxProb[i]);

                // this is needed for tool tips
                // for each tile in the box allocate a probability to it
                //foreach (SolverTile tile in this.boxes[i].GetTiles()) {
                //    tile.SetProbability(this.boxProb[i]);
                //}

            }


            // add the dead locations we found
            /*
            foreach (DeadCandidate dc in this.deadCandidates) {
                if (!dc.IsAlive() && dc.GetMyBox().GetProbability() != 0) {   // if it is dead and not a definite flag 
                        information.Write(dc.GetCandidate().AsText() + " is dead");
                    //this.deadTiles.Add(dc.GetCandidate());
                    information.SetTileToDead(dc.GetCandidate());   // set the tile as dead. A dead tile can never be resurrected.
                }
            }
            */

            /*
            for (int i = 0; i < hashTally.length; i++) {
                //solver.display(boxes.get(i).getSquares().size() + " " + boxes.get(i).getSquares().get(0).display() + " " + hashTally[i].toString());
                for (int j = i + 1; j < hashTally.length; j++) {

                    //BigInteger hash1 = hashTally[i].divide(BigInteger.valueOf(boxes.get(i).getSquares().size()));
                    //BigInteger hash2 = hashTally[j].divide(BigInteger.valueOf(boxes.get(j).getSquares().size()));

                    if (hashTally[i].compareTo(hashTally[j]) == 0 && boxes.get(i).getSquares().size() == 1 && boxes.get(j).getSquares().size() == 1) {
                        //if (hash1.compareTo(hash2) == 0) {
                        addLinkedLocation(linkedLocations, boxes.get(i), boxes.get(j));
                        addLinkedLocation(linkedLocations, boxes.get(j), boxes.get(i));
                        //solver.display("Box " + boxes.get(i).getSquares().get(0).display() + " is linked to Box " + boxes.get(j).getSquares().get(0).display() + " prob " + boxProb[i]);
                    }

                    // if one hasTally is the negative of the other then   i flag <=> j clear
                    if (hashTally[i].compareTo(hashTally[j].negate()) == 0 && boxes.get(i).getSquares().size() == 1 && boxes.get(j).getSquares().size() == 1) {
                        //if (hash1.compareTo(hash2.negate()) == 0) {
                        //solver.display("Box " + boxes.get(i).getSquares().get(0).display() + " is contra linked to Box " + boxes.get(j).getSquares().get(0).display() + " prob " + boxProb[i] + " " + boxProb[j]);
                        addLinkedLocation(contraLinkedLocations, boxes.get(i), boxes.get(j));
                        addLinkedLocation(contraLinkedLocations, boxes.get(j), boxes.get(i));
                    }
                }
            }
            */

            // sort so that the locations with the most links are at the top
            //Collections.sort(linkedLocations, LinkedLocation.SORT_BY_LINKS_DESC);

            // avoid divide by zero
            if (this.tilesOffEdge != 0 && totalTally != 0) {
                //offEdgeProbability = 1 - outsideTally / (totalTally * BigInt(this.squaresLeft));
                this.offEdgeProbability = 1 - Combination.DivideBigIntegerToDouble (outsideTally, totalTally * new BigInteger(this.tilesOffEdge), 6);
            } else {
                this.offEdgeProbability = 0;
            }

            this.finalSolutionCount = totalTally * solutionCountMultiplier;

            // see if we can find a guess which is better than outside the boxes
            double hwm = this.offEdgeProbability;

            //offEdgeBest = true;

            foreach (Box b in this.boxes) {

                // a box is dead if all its tiles are dead
                bool boxLiving = false;
                foreach (SolverTile tile in b.GetTiles()) {
                    if (!tile.IsDead()) {
                        boxLiving = true;
                        break;
                    }
                }
                //double prob = this.boxProb[b.GetUID()];
                double prob = b.GetProbability();
                if (boxLiving || prob == 1) {   // if living or 100% safe then consider this probability
                    if (hwm <= prob) {
                        hwm = prob;
                    }
                }
            }

            boxes.Sort(Box.CompareByProbabilityDescending);

            this.bestProbability = hwm;

            information.Write("Off edge probability is " + this.offEdgeProbability);
            information.Write("Best probability is " + this.bestProbability);
            information.Write("Game has  " + this.finalSolutionCount + " candidate solutions");

            //Console.WriteLine("Calculate probability took " + (DateTime.Now.Ticks - start) + " ticks");

        }

        public List<SolverAction> GetBestCandidates(double freshhold) {

            List<SolverAction> best = new List<SolverAction>();

            //solver.display("Squares left " + this.squaresLeft + " squares analysed " + web.getSquares().size());

            // if the outside probability is the best then return an empty list
            double test;
            //if (offEdgeBest) {
            //	solver.display("Best probability is off the edge " + bestProbability + " but will look for options on the edge only slightly worse");
            //	//test = bestProbability.multiply(Solver.EDGE_TOLERENCE);
            //	test = bestProbability.multiply(freshhold);
            //} else 

            if (this.bestProbability == 1) {  // if we have a probability of one then don't allow lesser probs to get a look in
                test = this.bestProbability;
            } else {
                test = this.bestProbability * freshhold;
            }

            information.Write("Best probability is " + this.bestProbability + " freshhold is " + test);

            foreach (Box b in boxes) {
                information.Write("Box has probability " + b.GetProbability());
                if (b.GetProbability() >= test) {
                    foreach (SolverTile tile in b.GetTiles()) {

                        if (!tile.IsDead() || b.GetProbability() == 1) {  // if the tile isn't dead or is certainly safe use it
                            if (b.GetProbability() == 0) {  // edge case here when the only non-dead tiles left happen to be mines
                                information.MineFound(tile);
                                best.Add(new SolverAction(tile, ActionType.Flag, b.GetProbability()));
                            } else {
                                best.Add(new SolverAction(tile, ActionType.Clear, b.GetProbability()));
                            }
                        }

                    }
                } else {
                    break;
                }
            }

            /*
            for (int i = 0; i < this.boxProb.Length; i++) {
                if (this.boxProb[i] >= test) {
                    foreach (SolverTile tile in this.boxes[i].GetTiles()) {

                        if (!tile.IsDead()) {
                            if (this.boxProb[i] == 0) {  // edge case here when the only non-dead tiles left happen to be mines
                                information.MineFound(tile);
                                best.Add(new SolverAction(tile, ActionType.Flag, this.boxProb[i]));
                            } else {
                                best.Add(new SolverAction(tile, ActionType.Clear, this.boxProb[i]));
                            }
                        }

                    }
                }
            }
            */

            // sort in to best order
            best.Sort();

            return best;

        }

        // probability that any square off the edge is a mine
        public double GetOffEdgeProbability() {
            return this.offEdgeProbability;
        }

        public BigInteger GetSolutionCount() {
            return this.finalSolutionCount;
        }

        public int GetSolutionCountMagnitude() {
            return (int) Math.Floor(BigInteger.Log10(this.finalSolutionCount));
        }

        // returns the propability of this tile being safe 
        public double GetProbability(SolverTile tile) {

            boxLookup.TryGetValue(tile, out Box box);
            if (box == null) {
                return GetOffEdgeProbability();
            } else {
                return box.GetProbability();
            }

        }

        // returns an array of 'SolverTile' which are clear based on analysis of their independent edge only
        public List<SolverTile> GetLocalClears() {
            return this.localClears;
        }

    }

     /*
     * Used to hold a solution
     */
    public class ProbabilityLine : IComparable<ProbabilityLine> {

        private int mineCount = 0;
        private BigInteger solutionCount;
        private BigInteger[] mineBoxCount; 

        public ProbabilityLine(int boxCount) {
            this.mineCount = 0;
            this.solutionCount = 0;
            this.mineBoxCount = new BigInteger[boxCount];
        }

        public void SetSolutionCount(BigInteger solutionCount) {
            this.solutionCount = solutionCount;
        }

        public BigInteger GetSolutionCount() {
            return this.solutionCount;
        }

        public BigInteger GetMineBoxCount(int i) {
            return mineBoxCount[i];
        }

        public void SetMineBoxCount(int i, BigInteger n) {
            this.mineBoxCount[i] = n;
        }

        public int GetMineCount() {
            return this.mineCount;
        }

        public void SetMineCount(int count) {
            this.mineCount = count;
        }

        public void CopyMineBoxCount(ProbabilityLine pl) {
            Array.Copy(pl.mineBoxCount, this.mineBoxCount, this.mineBoxCount.Length);
        }

        // sort by the number of mines in the solution
        public int CompareTo(ProbabilityLine o) {
            return this.mineCount - o.mineCount;
        }

    }

    public class EdgeStore {

        public readonly List<ProbabilityLine> data;

        public EdgeStore(List<ProbabilityLine> data) {
            this.data = data;
        }

        public static int SortByLineCount(EdgeStore a, EdgeStore b) {
            return a.data.Count - b.data.Count;
        }

    }

    // used to hold what we need to analyse next
    public class NextWitness {

        private BoxWitness boxWitness;
        private List<Box> oldBoxes = new List<Box>();
        private List<Box> newBoxes = new List<Box>();

        public NextWitness(BoxWitness boxWitness) {

            this.boxWitness = boxWitness;

            boxWitness.GetTile().SetExamined();   // set this tile as now longer new growth

            foreach (Box box in this.boxWitness.GetBoxes()) {

                 if (box.IsProcessed()) {
                    this.oldBoxes.Add(box);
                } else {
                    this.newBoxes.Add(box);
                }
            }
        }

        public BoxWitness GetBoxWitness() {
            return this.boxWitness;
        }

        public List<Box> GetNewBoxes() {
            return this.newBoxes;
        }

        public List<Box> GetOldBoxes() {
            return this.oldBoxes;
        }


}



    // holds a witness and all the Boxes adjacent to it
    public class BoxWitness {

        private readonly SolverTile tile;

        private readonly List<Box> boxes = new List<Box>();                 // adjacent boxes being witnessed
        private readonly List<SolverTile> tiles = new List<SolverTile>();   // adjacent tiles being witnessed

        private bool processed = false;
        private readonly int minesToFind;

        public BoxWitness(SolverInfo information, SolverTile tile) {

            this.tile = tile;

            this.minesToFind = tile.GetValue();

            List<SolverTile> adjTiles = information.GetAdjacentTiles(tile);

            // determine how many mines are left to find and store adjacent tiles
            foreach (SolverTile adjTile in adjTiles) {
                if (adjTile.IsMine()) {
                    this.minesToFind--;
                } else if (adjTile.IsHidden()) {
                    this.tiles.Add(adjTile);
                }
            }
        }

        public bool IsProcessed() {
            return processed;
        }

        public void SetProcessed(bool processed) {
            this.processed = processed;
        }

        public bool HasNewGrowth() {
            return tile.IsNewGrowth();
        }

        // gets the tile which is the witness
        public SolverTile GetTile() {
            return tile;
        }

        public List<SolverTile> GetAdjacentTiles() {
            return this.tiles;
        }

        // returns the boxes that are witnessed by this BoxWitness
        public List<Box> GetBoxes() {
            return this.boxes;
        }

        public int GetMinesToFind() {
            return this.minesToFind;
        }

        public bool Overlap(BoxWitness boxWitness) {

            // if the locations are too far apart they can't share any of the same squares
            if (Math.Abs(boxWitness.tile.x - this.tile.x) > 2 || Math.Abs(boxWitness.tile.y - this.tile.y) > 2) {
                return false;
            }

            foreach (SolverTile tile1 in boxWitness.tiles) {
                 foreach (SolverTile tile2 in this.tiles) {
                    if (tile1.IsEqual(tile2)) {  // if they share a tile then return true
                        return true;
                    }
                }
            }

            // no shared tile found
            return false;

        }


        // if two witnesses have the same Squares around them they are equivalent
        public bool Equivalent(BoxWitness boxWitness) {

            // if the number of squares is different then they can't be equivalent
            if (this.tiles.Count != boxWitness.tiles.Count) {
                return false;
            }

            // if the locations are too far apart they can't share the same squares
            if (Math.Abs(boxWitness.tile.x - this.tile.x) > 2 || Math.Abs(boxWitness.tile.y - this.tile.y) > 2) {
                return false;
            }

            foreach (SolverTile l1 in this.tiles) {

                bool found = false;
                foreach (SolverTile l2 in boxWitness.tiles) {
                    if (l1.IsEqual(l2)) {
                        found = true;
                        break;
                    }
                }
                if (!found) {
                    return false;
                }
            }

            return true;
        }

        // add an adjacdent box 
        public void AddBox(Box box) {
            this.boxes.Add(box);
        }
    }

    // information about the boxes surrounding a dead candidate
    public class DeadCandidate {

        private readonly SolverTile candidate;
        private readonly Box myBox;
        private bool isAlive = false;
        private List<Box> goodBoxes = new List<Box>();
        private List<Box> badBoxes = new List<Box>();
        private bool firstCheck = true;
        private BigInteger total;

        public DeadCandidate(SolverTile tile, Box myBox) {

            this.candidate = tile;
            this.myBox = myBox;

        }

        public SolverTile GetCandidate() {
            return this.candidate;
        }

        public bool IsAlive() {
            return this.isAlive;
        }

        public void SetAlive() {
            this.isAlive = true;
        }

        public BigInteger GetTotal() {
            return this.total;
        }

        public Box GetMyBox() {
            return this.myBox;
        }

        // checks that the total is the same as previous totals
        public bool CheckTotalOkay(BigInteger total) {
            if (firstCheck) {
                this.total = total;
                firstCheck = false;
                return true;
            } else {
                if (this.total != total) {
                    return false;
                } else {
                    return true;
                }
            }
        }

        public List<Box> GetGoodBoxes() {
            return this.goodBoxes;
        }

        public void AddToGoodBoxes(Box b) {
            this.goodBoxes.Add(b);
        }

        public List<Box> GetBadBoxes() {
            return this.badBoxes;
        }

        public void AddToBadBoxes(Box b) {
            this.badBoxes.Add(b);
        }


    }

    // a box is a group of tiles which share the same witnesses
    public class Box {

        private bool processed = false;
        private readonly int uid;
        private int minMines;
        private int maxMines;
        private bool dead = true;    // a box is dead if all its tiles are dead

        private double safeProbability;

        private readonly List<BoxWitness> boxWitnesses = new List<BoxWitness>();

        private readonly List<SolverTile> tiles = new List<SolverTile>();

        public Box(List<BoxWitness> boxWitnesses, SolverTile tile, int uid) {

            this.uid = uid;

            this.tiles.Add(tile);

            if (!tile.IsDead()) {
                dead = false;
            }

            foreach (BoxWitness bw in boxWitnesses) {
                if (tile.IsAdjacent(bw.GetTile())) {
                    this.boxWitnesses.Add(bw);
                    bw.AddBox(this);

                }
            }

            //console.log("Box created for tile " + tile.asText() + " with " + this.boxWitnesses.length + " witnesses");

        }

        public bool IsProcessed() {
            return this.processed;
        }

        public void SetProcessed(bool processed) {
            this.processed = processed;
        }

        public int GetMinMines() {
            return this.minMines;
        }

        public int GetMaxMines() {
            return this.maxMines;
        }

        public int GetUID() {
            return this.uid;
        }

        public List<SolverTile> GetTiles() {
            return this.tiles;
        }

        public List<BoxWitness> GetBoxWitnesses() {
            return this.boxWitnesses;
        }

        public double GetProbability() {
            return this.safeProbability;
        }

        public void SetProbability(double probability) {
            this.safeProbability = probability;
        }

        public bool IsDead() {
            return this.dead;
        }

        // if the tiles surrounding witnesses equal the boxes then it fits
        public bool Fits(SolverTile tile, int count) {

            // a tile can't share the same witnesses for this box if they have different numbers
            if (count != this.boxWitnesses.Count) {
                return false;
            }

            foreach (BoxWitness bw in this.boxWitnesses) {
                if (!bw.GetTile().IsAdjacent(tile)) {
                    return false;
                }
            }

            //console.log("Tile " + tile.asText() + " fits in box with tile " + this.tiles[0].asText());

            return true;

        }

        /*
        * Once all the squares have been added we can do some calculations
        */
        public void Calculate(int minesLeft) {

            this.maxMines = Math.Min(this.tiles.Count, minesLeft);  // can't have more mines then there are tiles to put them in or mines left to discover
            this.minMines = 0;

            foreach (BoxWitness bw in this.boxWitnesses) {
                if (bw.GetMinesToFind() < this.maxMines) {  // can't have more mines than the lowest constraint
                    this.maxMines = bw.GetMinesToFind();
                }
            }

        }

        // add a new tile to the box
        public void Add(SolverTile tile) {
            this.tiles.Add(tile);
            if (!tile.IsDead()) {
                dead = false;
            }
        }

        public bool Contains(SolverTile tile) {

            // return true if the given tile is in this box
            foreach (SolverTile tile1 in this.tiles) {
                if (tile1.IsEqual(tile)) {
                    return true;
                }
            }

            return false;

        }

        public static int CompareByProbabilityDescending(Box x, Box y) {

            return -x.safeProbability.CompareTo(y.safeProbability);
            
        }
    }

}
