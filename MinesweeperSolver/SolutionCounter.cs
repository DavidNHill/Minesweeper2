using System;
using System.Collections.Generic;
using System.Numerics;
using static MinesweeperControl.MinesweeperGame;

namespace MinesweeperSolver {
    public class SolutionCounter {

        private readonly int[][] SMALL_COMBINATIONS = new int[][] { new int[] { 1 }, new int[] { 1, 1 }, new int[] { 1, 2, 1 }, new int[] { 1, 3, 3, 1 }, new int[] { 1, 4, 6, 4, 1 }, new int[] { 1, 5, 10, 10, 5, 1 }, new int[] { 1, 6, 15, 20, 15, 6, 1 }, new int[] { 1, 7, 21, 35, 35, 21, 7, 1 }, new int[] { 1, 8, 28, 56, 70, 56, 28, 8, 1 } };

        private readonly List<SolverTile> witnessed;
        private readonly List<BoxWitness> prunedWitnesses = new List<BoxWitness>();  // a subset of allWitnesses with equivalent witnesses removed
        private readonly List<Box> boxes = new List<Box>();
        private readonly List<BoxWitness> boxWitnesses = new List<BoxWitness>();
        private bool[] mask;

        private readonly Dictionary<SolverTile, Box> boxLookup = new Dictionary<SolverTile, Box>();  // a lookup which finds which box a tile belongs to (an efficiency enhancement)
        //private readonly List<DeadCandidate> deadCandidates = new List<DeadCandidate>();

        private List<ProbabilityLine> workingProbs = new List<ProbabilityLine>();
        private readonly List<ProbabilityLine> heldProbs = new List<ProbabilityLine>();
        private readonly List<EdgeStore> edgeStore = new List<EdgeStore>(); // stores independent edge analysis until we know we have to merge them


        private readonly int minesLeft;
        private readonly int tilesLeft;
        private readonly int tilesOffEdge;
        private readonly int minTotalMines;
        private readonly int maxTotalMines;
        private int recursions;

        // used to find the range of mine counts which offer the 2.5% - 97.5% weighted average
        private int edgeMinesMin;
        private int edgeMinesMax;
        private int edgeMinesMinLeft;
        private int edgeMinesMaxLeft;
        private int mineCountUpperCutoff;
        private int mineCountLowerCutoff;
        private bool truncatedProbs = false;   // this gets set when the number of held probabilies exceeds the permitted threshold

        private BigInteger finalSolutionCount = 0;
        private BigInteger solutionCountMultiplier = 1;

        private int clearCount = 0;

        private readonly SolverInfo information;

        public SolutionCounter(SolverInfo information, List<SolverTile> allWitnesses, List<SolverTile> allWitnessed, int squaresLeft, int minesLeft) {

            this.information = information;

            this.witnessed = allWitnessed;

            // constraints in the game
            this.minesLeft = minesLeft;
            this.tilesLeft = squaresLeft;
            this.tilesOffEdge = squaresLeft - allWitnessed.Count;   // squares left off the edge and unrevealed
            this.minTotalMines = minesLeft - this.tilesOffEdge;     //we can't use so few mines that we can't fit the remainder elsewhere on the board
            this.maxTotalMines = minesLeft;

            this.mineCountUpperCutoff = minesLeft;
            this.mineCountLowerCutoff = minTotalMines;

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
                //foreach (SolverTile adjTile in information.GetAdjacentTiles(tile)) {
                //    if (information.GetWitnesses().Contains(adjTile)) {
                //        count++;
                //    }
                //}

                // count how many adjacent witnesses the tile has
                foreach (SolverTile tile1 in allWitnesses) {
                    if (tile.IsAdjacent(tile1)) {
                       count++;
                    }
                }

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
  
            // create an initial solution of no mines anywhere 
            ProbabilityLine held = new ProbabilityLine(this.boxes.Count);
            held.SetSolutionCount(1);
            this.heldProbs.Add(held);

            // add an empty probability line to get us started
            this.workingProbs.Add(new ProbabilityLine(this.boxes.Count));

            NextWitness nextWitness = FindFirstWitness();

            while (nextWitness != null) {

                // mark the new boxes as processed - which they will be soon
                foreach (Box box in nextWitness.GetNewBoxes()) {
                    this.mask[box.GetUID()] = true;
                }

                this.workingProbs = MergeProbabilities(nextWitness);

                nextWitness = FindNextWitness(nextWitness);

            }

            CalculateBoxProbabilities();

        }


        // take the next witness details and merge them into the currently held details
        private List<ProbabilityLine> MergeProbabilities(NextWitness nw) {

            List<ProbabilityLine> newProbs = new List<ProbabilityLine>();

            foreach (ProbabilityLine pl in this.workingProbs) {

                int missingMines = nw.GetBoxWitness().GetMinesToFind() - (int)CountPlacedMines(pl, nw);

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
            if (this.recursions % 100000 == 0) {
                information.Write("Solution Counter recursision = " + recursions);
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
 
            }

            return result;

        }

        // create a new probability line by taking the old and adding the mines to the new Box
        private ProbabilityLine ExtendProbabilityLine(ProbabilityLine pl, Box newBox, int mines) {

            //console.log("Extended probability line: Adding " + mines + " mines to box " + newBox.uid);
            //console.log("Extended probability line before" + pl.mineBoxCount);

            ProbabilityLine result = new ProbabilityLine(this.boxes.Count);

            result.SetMineCount(pl.GetMineCount() + mines);

            result.CopyMineBoxCount(pl);

            result.SetMineBoxCount(newBox.GetUID(), new BigInteger(mines));

            //console.log("Extended probability line after " + result.mineBoxCount);

            return result;
        }

        // here we store the information we have found about this independent edge for later use
        private void StoreProbabilities() {

            List<ProbabilityLine> crunched = CrunchByMineCount(this.workingProbs);

            //Console.WriteLine("Edge has " + crunched.Count + " probability lines after consolidation");

            edgeStore.Add(new EdgeStore(crunched, this.mask));  // store the existing data 

            workingProbs.Clear();  // and get a new list for the next independent edge

            this.workingProbs.Add(new ProbabilityLine(this.boxes.Count));  // add a new starter probability line

            this.mask = new bool[boxes.Count];
 
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

            int mineCountMin = workingProbs[0].GetMineCount() + heldProbs[0].GetMineCount();

            // shrink the window 
            edgeMinesMinLeft = edgeMinesMinLeft - workingProbs[0].GetMineCount();
            edgeMinesMaxLeft = edgeMinesMaxLeft - workingProbs[workingProbs.Count - 1].GetMineCount();

            foreach (ProbabilityLine pl in workingProbs) {

                BigInteger plSolCount = pl.GetSolutionCount() / hcd;

                foreach (ProbabilityLine epl in heldProbs) {

                    // if the mine count can never reach the lower cuttoff then ignore it
                    if (pl.GetMineCount() + epl.GetMineCount() + edgeMinesMaxLeft < this.mineCountLowerCutoff) {
                        continue;
                    }

                    // if the mine count will always be pushed beyonf the upper cuttoff then ignore it
                    if (pl.GetMineCount() + epl.GetMineCount() + edgeMinesMinLeft > this.mineCountUpperCutoff) {
                        continue;
                    }

                    ProbabilityLine newpl = new ProbabilityLine(this.boxes.Count);

                    newpl.SetMineCount(pl.GetMineCount() + epl.GetMineCount());

                    BigInteger eplSolCount = epl.GetSolutionCount() / hcd;

                    newpl.SetSolutionCount(pl.GetSolutionCount() * eplSolCount);

                    for (int k = 0; k < this.boxes.Count; k++) {

                        BigInteger w1 = pl.GetMineBoxCount(k) * eplSolCount;
                        BigInteger w2 = epl.GetMineBoxCount(k) * plSolCount;
                        newpl.SetMineBoxCount(k, w1 + w2);

                    }

                    result.Add(newpl);

                }
            }

            //Console.WriteLine("Solution multiplier is " + solutionCountMultiplier);

            this.heldProbs.Clear();

            // if result is empty this is an impossible position
            if (result.Count == 0) {
                Console.WriteLine("Impossible position encountered");
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

                    this.heldProbs.Add(npl);
                    mc = pl.GetMineCount();
                    npl = new ProbabilityLine(this.boxes.Count);
                    npl.SetMineCount(mc);
                }
                npl.SetSolutionCount(npl.GetSolutionCount() + pl.GetSolutionCount());

                for (int j = 0; j < this.boxes.Count; j++) {
                    npl.SetMineBoxCount(j, npl.GetMineBoxCount(j) + pl.GetMineBoxCount(j));
                }
            }

            this.heldProbs.Add(npl);

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
 
            return result;

        }

        // calculate how many ways this solution can be generated and roll them into one
        private void MergeLineProbabilities(ProbabilityLine npl, ProbabilityLine pl) {

            BigInteger solutions = 1;
            for (int i = 0; i < this.boxes.Count; i++) {
                solutions = solutions * (BigInteger)SMALL_COMBINATIONS[this.boxes[i].GetTiles().Count][(int)pl.GetMineBoxCount(i)];
            }

            npl.SetSolutionCount(npl.GetSolutionCount() + solutions);

            for (int i = 0; i < this.boxes.Count; i++) {
                if (this.mask[i]) {  // if this box has been involved in this solution - if we don't do this the hash gets corrupted by boxes = 0 mines because they weren't part of this edge
                    npl.SetMineBoxCount(i, npl.GetMineBoxCount(i) + pl.GetMineBoxCount(i) * solutions);
                }
            }

        }

        // return any witness which hasn't been processed
        private NextWitness FindFirstWitness() {

            BoxWitness excluded = null;
            foreach (BoxWitness boxWit in this.boxWitnesses) {
                if (!boxWit.IsProcessed()) {
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

            //independentGroups++;

            // since we have calculated all the mines in an independent set of witnesses we can crunch them down and store them for later

            // store the details for this edge
            StoreProbabilities();


            // get an unprocessed witness
            NextWitness nw = FindFirstWitness();
            if (nw != null) {
                information.Write("Starting a new independent edge");
            }

            // If there is nothing else to process then either do the local clears or calculate the probabilities
            if (nw == null) {

                edgeStore.Sort(EdgeStore.SortByLineCount);

                AnalyseAllEdges();

                long start = DateTime.Now.Ticks;
                foreach (EdgeStore edgeDetails in edgeStore) {
                    workingProbs = edgeDetails.data;
                    CombineProbabilities();
                }
                information.Write("Combined all edges in " + (DateTime.Now.Ticks - start) + " ticks");

            }

            // return the next witness to process
            return nw;

        }

        // take a look at what edges we have and show some information - trying to get some ideas on how we can get faster good guesses
        private void AnalyseAllEdges() {

            //Console.WriteLine("Number of tiles off the edge " + tilesOffEdge);
            //Console.WriteLine("Number of mines to find " + minesLeft);

            this.edgeMinesMin = 0;
            this.edgeMinesMax = 0;

            foreach (EdgeStore edge in edgeStore) {
                int edgeMinMines = edge.data[0].GetMineCount();
                int edgeMaxMines = edge.data[edge.data.Count - 1].GetMineCount();

                edgeMinesMin = edgeMinesMin + edgeMinMines;
                edgeMinesMax = edgeMinesMax + edgeMaxMines;
            }

            information.Write("Min mines on all edges " + edgeMinesMin + ", max " + edgeMinesMax);
            this.edgeMinesMaxLeft = this.edgeMinesMax;  // these values are used  in the merge logic to reduce the number of lines need to keep
            this.edgeMinesMinLeft = this.edgeMinesMin;

            this.mineCountLowerCutoff = this.edgeMinesMin;
            this.mineCountUpperCutoff = Math.Min(this.edgeMinesMax, this.minesLeft);  // can't have more mines than are left

            // comment this out when doing large board analysis
            return;

            // the code below reduces the range of mine count values to just be the 'significant' range 

            List<ProbabilityLine> store = new List<ProbabilityLine>();
            List<ProbabilityLine> store1 = new List<ProbabilityLine>();

            ProbabilityLine init = new ProbabilityLine(0);
            init.SetSolutionCount(1);

            store.Add(init);

            // combine all the edges to determine the relative weights of the mine count
            foreach (EdgeStore edgeDetails in edgeStore) {

                foreach (ProbabilityLine pl in edgeDetails.data) {

                    BigInteger plSolCount = pl.GetSolutionCount();

                    foreach (ProbabilityLine epl in store) {

                        if (pl.GetMineCount() + epl.GetMineCount() <= this.maxTotalMines) {

                            ProbabilityLine newpl = new ProbabilityLine(0);

                            newpl.SetMineCount(pl.GetMineCount() + epl.GetMineCount());

                            BigInteger eplSolCount = epl.GetSolutionCount();

                            newpl.SetSolutionCount(pl.GetSolutionCount() * eplSolCount);

                            store1.Add(newpl);

                        }
                    }
                }

                store.Clear();

                // sort into mine order 
                store1.Sort();

                int mc = store1[0].GetMineCount();
                ProbabilityLine npl = new ProbabilityLine(0);
                npl.SetMineCount(mc);

                foreach (ProbabilityLine pl in store1) {

                    if (pl.GetMineCount() != mc) {

                        store.Add(npl);
                        mc = pl.GetMineCount();
                        npl = new ProbabilityLine(0);
                        npl.SetMineCount(mc);
                    }
                    npl.SetSolutionCount(npl.GetSolutionCount() + pl.GetSolutionCount());

                }

                store.Add(npl);
                store1.Clear();
            }

            BigInteger total = 0;
            int mineValues = 0;
            foreach (ProbabilityLine pl in store) {
                if (pl.GetMineCount() >= this.minTotalMines) {    // if the mine count for this solution is less than the minimum it can't be valid
                    BigInteger mult = SolverMain.Calculate(this.minesLeft - pl.GetMineCount(), this.tilesOffEdge);  //# of ways the rest of the board can be formed
                    total = total + mult * pl.GetSolutionCount();
                    mineValues++;
                }
            }

            //this.mineCountLowerCutoff = this.edgeMinesMin;
            //this.mineCountUpperCutoff = Math.Min(this.edgeMinesMax, this.minesLeft);  // can't have more mines than are left
            BigInteger soFar = 0;
            foreach (ProbabilityLine pl in store) {
                if (pl.GetMineCount() >= this.minTotalMines) {    // if the mine count for this solution is less than the minimum it can't be valid
                    BigInteger mult = SolverMain.Calculate(this.minesLeft - pl.GetMineCount(), this.tilesOffEdge);  //# of ways the rest of the board can be formed
                    soFar = soFar + mult * pl.GetSolutionCount();
                    double perc = Combination.DivideBigIntegerToDouble(soFar, total, 6) * 100;
                    //Console.WriteLine("Mine count " + pl.GetMineCount() + " has solution count " + pl.GetSolutionCount() + " multiplier " + mult + " running % " + perc);
                    //Console.WriteLine("Mine count " + pl.GetMineCount() + " has solution count " + pl.GetSolutionCount() + " has running % " + perc);

                    if (mineValues > 30 && perc < 2.5) {
                        this.mineCountLowerCutoff = pl.GetMineCount();
                    }

                    if (mineValues > 30 && perc > 97.5) {
                        this.mineCountUpperCutoff = pl.GetMineCount();
                        break;
                    }
                }
            }

            information.Write("Significant range " + this.mineCountLowerCutoff + " - " + this.mineCountUpperCutoff);
            //this.edgeMinesMaxLeft = this.edgeMinesMax;
            //this.edgeMinesMinLeft = this.edgeMinesMin;


            return;

            // below here are experimental ideas on getting a good guess on very large boards

            int midRangeAllMines = (this.mineCountLowerCutoff + this.mineCountUpperCutoff) / 2;
            BigInteger[] tally = new BigInteger[boxes.Count];
            double[] probability = new double[boxes.Count];


            foreach (EdgeStore edgeDetails in edgeStore) {

                int sizeRangeEdgeMines = (edgeDetails.data[edgeDetails.data.Count - 1].GetMineCount() - edgeDetails.data[0].GetMineCount()) / 2;

                int start = (this.mineCountLowerCutoff - edgeDetails.data[0].GetMineCount() + this.mineCountUpperCutoff - edgeDetails.data[edgeDetails.data.Count - 1].GetMineCount()) / 2;
                //int start = midRangeAllMines - sizeRangeEdgeMines;

                //BigInteger mult = Combination.Calculate(this.minesLeft - start, this.tilesOffEdge);
                BigInteger totalTally = 0;

                foreach (ProbabilityLine pl in edgeDetails.data) {

                    BigInteger mult = Combination.Calculate(this.minesLeft - start - pl.GetMineCount(), this.tilesOffEdge);
                    totalTally += mult * pl.GetSolutionCount();
                    for (int i = 0; i < boxes.Count; i++) {
                        if (edgeDetails.mask[i]) {
                            BigInteger work = pl.GetMineBoxCount(i) * mult;
                            tally[i] += work;
                        }
                    }

                    //mult = mult * (this.tilesOffEdge - start) / (start + 1);
                    //start++;

                }

                for (int i = 0; i < boxes.Count; i++) {
                    if (edgeDetails.mask[i]) {
                        probability[i] = Combination.DivideBigIntegerToDouble(tally[i], totalTally, 6) / boxes[i].GetTiles().Count;
                    }
                }

                int minIndex = -1;
                for (int i = 0; i < boxes.Count; i++) {
                    if (edgeDetails.mask[i]) {
                        if (minIndex == -1 || probability[i] < probability[minIndex]) {
                            minIndex = i;
                        }
                    }
                }

                if (minIndex != -1) {
                    information.Write("Best guess is " + boxes[minIndex].GetTiles()[0].AsText() + " with " + (1 - probability[minIndex]));
                } else {
                    information.Write("No Guess found");
                }

            }


        }

        // here we expand the localised solution to one across the whole board and
        // sum them together to create a definitive probability for each box
        private void CalculateBoxProbabilities() {

            //long start = DateTime.Now.Ticks;

            if (truncatedProbs) {
                Console.WriteLine("probability line combining was truncated");
            }

            information.Write("Solution count multiplier is " + this.solutionCountMultiplier);

            BigInteger[] tally = new BigInteger[this.boxes.Count];

            // total game tally
            BigInteger totalTally = 0;

            // outside a box tally
            BigInteger outsideTally = 0;

            //console.log("There are " + this.heldProbs.length + " different mine counts on the edge");

            bool[] emptyBox = new bool[boxes.Count];
            for (int i = 0; i < emptyBox.Length; i++) {
                emptyBox[i] = true;
            }

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

                    for (int i = 0; i < emptyBox.Length; i++) {
                        if (pl.GetMineBoxCount(i) != 0) {
                            emptyBox[i] = false;
                        }
                    }

                }

            }

            // determine how many clear squares there are
            if (totalTally > 0) {
                for (int i = 0; i < emptyBox.Length; i++) {
                    if (emptyBox[i]) {
                        clearCount = clearCount + boxes[i].GetTiles().Count;
                    }
                }
            }

            this.finalSolutionCount = totalTally * solutionCountMultiplier;

             information.Write("Game has  " + this.finalSolutionCount + " candidate solutions");

        }

        public BigInteger GetSolutionCount() {
            return this.finalSolutionCount;
        }

        public int getClearCount() {
            return this.clearCount;
        }

        public int GetSolutionCountMagnitude() {
            return (int)Math.Floor(BigInteger.Log10(this.finalSolutionCount));
        }

        public SolverInfo GetSolverInfo() {
            return this.information;
        }

    }



}
