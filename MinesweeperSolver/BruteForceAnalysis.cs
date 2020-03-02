using System;
using System.Collections.Generic;
using System.Text;
using static MinesweeperControl.MinesweeperGame;

namespace MinesweeperSolver {

    public class BruteForceAnalysis {

        // used to hold all the solutions left in the game
        public class SolutionTable {

            private readonly object locker = new object();

            private readonly BruteForceAnalysis bfa;
            private readonly sbyte[][] solutions;
            private int size = 0;

            public SolutionTable(BruteForceAnalysis bfa, int maxSize) {
                this.bfa = bfa;
                solutions = new sbyte[maxSize][];
            }

            public void AddSolution(sbyte[] solution) {
                lock(locker) {
                    solutions[size] = solution;
                    size++;
                }
            }

            public int GetSize() {
                return size;
            }

            public sbyte[] Get(int index) {
                return solutions[index];
            }

            public void SortSolutions(int start, int end, int index) {

                Array.Sort(solutions, start, end - start, bfa.sorters[index]);

            }

        }

        /**
	     * This sorts solutions by the value of a position
	     */
        public class SortSolutions : IComparer<sbyte[]> {

            private readonly int sortIndex;

            public SortSolutions(int index) {
                sortIndex = index;
            }

            public int Compare(sbyte[] o1, sbyte[] o2) {
                return o1[sortIndex] - o2[sortIndex];
            }

        }

        /**
         * A key to uniquely identify a position
         */
        public class Position {

            private readonly byte[] position;
            private int hash;

            public Position(int size) {
                position = new byte[size];
                for (int i = 0; i < position.Length; i++) {
                    position[i] = 15;
                }
            }

            public Position(Position p, int index, int value) {
                // copy and update to reflect the new position
                position = new byte[p.position.Length];
                Array.Copy(p.position, position, p.position.Length);
                position[index] = (byte)(value + 50);
            }

            // copied from String hash
            public override int GetHashCode() {
                int h = hash;
                if (h == 0 && position.Length > 0) {
                    for (int i = 0; i < position.Length; i++) {
                        h = 31 * h + position[i];
                    }
                    hash = h;
                }
                return h;
            }

            public override bool Equals(Object o) {
                if (o is Position) {
                    for (int i = 0; i < position.Length; i++) {
                        if (this.position[i] != ((Position)o).position[i]) {
                            return false;
                        }
                    }
                    return true;
                } else {
                    return false;
                }
            }
        }



        /**
         * Positions on the board which can still reveal information about the game.
         */
        public class LivingLocation : IComparable<LivingLocation> {

            //private int winningLines = 0;
            public bool pruned = false;
            public readonly short index;
            public int mineCount = 0;  // number of remaining solutions which have a mine in this position
            public int maxSolutions = 0;    // the maximum number of solutions that can be remaining after clicking here
            public int zeroSolutions = 0;    // the number of solutions that have a '0' value here
            public sbyte maxValue = -1;
            public sbyte minValue = -1;
            public byte count;  // number of possible values at this location

            public Node[] children;
            private readonly BruteForceAnalysis bfa;

            public LivingLocation(BruteForceAnalysis bfa, short index) {
                this.index = index;
                this.bfa = bfa;
            }

            /**
             * Determine the Nodes which are created if we play this move. Up to 9 positions where this locations reveals a value [0-8].
             * @param location
             * @return
             */
            public void BuildChildNodes(Node parent) {

                // sort the solutions by possible values
                bfa.allSolutions.SortSolutions(parent.startLocation, parent.endLocation, this.index);
                int index = parent.startLocation;

                // skip over the mines
                while (index < parent.endLocation && bfa.allSolutions.Get(index)[this.index] == Cruncher.BOMB) {
                    index++;
                }

                Node[] work = new Node[9];
                for (int i = this.minValue; i < this.maxValue + 1; i++) {

                    // if the node is in the cache then use it
                    Position pos = new Position(parent.position, this.index, i);

                    if (!bfa.cache.TryGetValue(pos, out Node temp1)) {  // if value not in cache

                        Node temp = new Node(bfa, pos);

                        temp.startLocation = index;
                        // find all solutions for this values at this location
                        while (index < parent.endLocation && bfa.allSolutions.Get(index)[this.index] == i) {
                            index++;
                        }
                        temp.endLocation = index;

                        work[i] = temp;

                    } else {  // value in cache

                        //System.out.println("In cache " + temp.position.key + " " + temp1.position.key);
                        //if (!temp.equals(temp1)) {
                        //	System.out.println("Cache not equal!!");
                        //}
                        //temp1.fromCache = true;
                        work[i] = temp1;
                        bfa.cacheHit++;
                        bfa.cacheWinningLines = bfa.cacheWinningLines + temp1.winningLines;
                        // skip past these details in the array
                        while (index < parent.endLocation && bfa.allSolutions.Get(index)[this.index] <= i) {
                            index++;
                        }
                    }

                }

                if (index != parent.endLocation) {
                    Console.WriteLine("Didn't read all the elements in the array; index = " + index + " end = " + parent.endLocation);
                }


                for (int i = this.minValue; i <= this.maxValue; i++) {
                    if (work[i].GetSolutionSize() > 0) {
                        //if (!work[i].fromCache) {
                        //	work[i].determineLivingLocations(this.livingLocations, living.index);
                        //}
                    } else {
                        work[i] = null;   // if no solutions then don't hold on to the details
                    }

                }

                this.children = work;

            }

            public int CompareTo(LivingLocation o) {

                // return location most likely to be clear  - this has to be first, the logic depends upon it
                int test = this.mineCount - o.mineCount;
                if (test != 0) {
                    return test;
                }

                // then the location most likely to have a zero
                test = o.zeroSolutions - this.zeroSolutions;
                if (test != 0) {
                    return test;
                }

                // then by most number of different possible values
                test = o.count - this.count;
                if (test != 0) {
                    return test;
                }

                // then by the maxSolutions - ascending
                return this.maxSolutions - o.maxSolutions;

            }

        }

        /**
         * A representation of a possible state of the game
         */
        public class Node {

            public Position position;        // representation of the position we are analysing / have reached

            public int winningLines = 0;      // this is the number of winning lines below this position in the tree
            public int work = 0;              // this is a measure of how much work was needed to calculate WinningLines value
            private bool fromCache = false;    // indicates whether this position came from the cache

            public int startLocation;              // the first solution in the solution array that applies to this position
            public int endLocation;                // the last + 1 solution in the solution array that applies to this position

            public List<LivingLocation> livingLocations;   // these are the locations which need to be analysed

            public LivingLocation bestLiving;              // after analysis this is the location that represents best play

            private readonly BruteForceAnalysis bfa;

            public Node(BruteForceAnalysis bfa, int size) {
                position = new Position(size);
                this.bfa = bfa;
            }

            public Node(BruteForceAnalysis bfa, Position position) {
                this.position = position;
                this.bfa = bfa;
            }

            public List<LivingLocation> GetLivingLocations() {
                return livingLocations;
            }

            public int GetSolutionSize() {
                return endLocation - startLocation;
            }

            /**
             * Get the probability of winning the game from the position this node represents  (winningLines / solution size)
             * @return
             */
            public double GetProbability() {

                return ((double)winningLines) / GetSolutionSize();
                //return BigDecimal.valueOf(winningLines).divide(BigDecimal.valueOf(getSolutionSize()), Solver.DP, RoundingMode.HALF_UP);

            }

            /**
             * Calculate the number of winning lines if this move is played at this position
             * Used at top of the game tree
             */
            public int GetWinningLines(LivingLocation move) {

                //if we can never exceed the cutoff then no point continuing
                if (SolverMain.PRUNE_BF_ANALYSIS && this.GetSolutionSize() - move.mineCount <= this.winningLines) {
                    move.pruned = true;
                    return 0;
                }

                int winningLines = GetWinningLines(1, move, this.winningLines);

                if (winningLines > this.winningLines) {
                    this.winningLines = winningLines;
                }

                return winningLines;
            }


            /**
             * Calculate the number of winning lines if this move is played at this position
             * Used when exploring the game tree
             */
            public int GetWinningLines(int depth, LivingLocation move, int cutoff) {

                int result = 0;

                bfa.processCount++;
                if (bfa.processCount > SolverMain.BRUTE_FORCE_ANALYSIS_MAX_NODES) {
                    return 0;
                }

                int notMines = this.GetSolutionSize() - move.mineCount;

                move.BuildChildNodes(this);

                foreach (Node child in move.children) {

                    if (child == null) {
                        continue;  // continue the loop but ignore this entry
                    }

                    int maxWinningLines = result + notMines;

                    // if the max possible winning lines is less than the current cutoff then no point doing the analysis
                    if (SolverMain.PRUNE_BF_ANALYSIS && maxWinningLines <= cutoff) {
                        move.pruned = true;
                        return 0;
                    }


                    if (child.fromCache) {  // nothing more to do, since we did it before
                        this.work++;
                    } else {

                        child.DetermineLivingLocations(this.livingLocations, move.index);
                        this.work++;

                        if (child.GetLivingLocations().Count == 0) {  // no further information ==> all solution indistinguishable ==> 1 winning line

                            child.winningLines = 1;

                        } else {  // not cached and not terminal node, so we need to do the recursion

                            foreach (LivingLocation childMove in child.GetLivingLocations()) {

                                // if the number of safe solutions <= the best winning lines then we can't do any better, so skip the rest
                                if (child.GetSolutionSize() - childMove.mineCount <= child.winningLines) {
                                    break;
                                }

                                // now calculate the winning lines for each of these children
                                int winningLines = child.GetWinningLines(depth + 1, childMove, child.winningLines);
                                if (child.winningLines < winningLines || (child.bestLiving != null && child.winningLines == winningLines && child.bestLiving.mineCount < childMove.mineCount)) {
                                    child.winningLines = winningLines;
                                    child.bestLiving = childMove;
                                }

                                // if there are no mines then this is a 100% safe move, so skip any further analysis since it can't be any better
                                if (childMove.mineCount == 0) {
                                    break;
                                }


                            }

                            // no need to hold onto the living location once we have determined the best of them
                            child.livingLocations = null;

                            //if (depth > solver.preferences.BRUTE_FORCE_ANALYSIS_TREE_DEPTH) {  // stop holding the tree beyond this depth
                            //	child.bestLiving = null;
                            //}

                            // add the child to the cache if it didn't come from there and it is carrying sufficient winning lines
                            if (child.work > 30) {
                                child.work = 0;
                                child.fromCache = true;
                                bfa.cacheSize++;
                                bfa.cache.Add(child.position, child);
                            } else {
                                this.work = this.work + child.work;
                            }


                        }

                    }

                    if (depth > SolverMain.BRUTE_FORCE_ANALYSIS_TREE_DEPTH) {  // stop holding the tree beyond this depth
                        child.bestLiving = null;
                    }

                    // store the aggregate winning lines 
                    result = result + child.winningLines;

                    notMines = notMines - child.GetSolutionSize();  // reduce the number of not mines

                }

                return result;

            }

            /**
             * this generates a list of Location that are still alive, (i.e. have more than one possible value) from a list of previously living locations
             * Index is the move which has just been played (in terms of the off-set to the position[] array)
             */
            public void DetermineLivingLocations(List<LivingLocation> liveLocs, int index) {

                List<LivingLocation> living = new List<LivingLocation>(liveLocs.Count);

                foreach (LivingLocation live in liveLocs) {

                    if (live.index == index) {  // if this is the same move we just played then no need to analyse it - definitely now non-living.
                        continue;
                    }

                    int value;

                    int[] valueCount = bfa.ResetValues();
                    int mines = 0;
                    int maxSolutions = 0;
                    byte count = 0;
                    sbyte minValue = 0;
                    sbyte maxValue = 0;

                    for (int j = startLocation; j < endLocation; j++) {
                        value = bfa.allSolutions.Get(j)[live.index];
                        if (value != Cruncher.BOMB) {
                            //values[value] = true;
                            valueCount[value]++;
                        } else {
                            mines++;
                        }
                    }

                    // find the new minimum value and maximum value for this location (can't be wider than the previous min and max)
                    for (sbyte j = live.minValue; j <= live.maxValue; j++) {
                        if (valueCount[j] > 0) {
                            if (count == 0) {
                                minValue = j;
                            }
                            maxValue = j;
                            count++;
                            if (maxSolutions < valueCount[j]) {
                                maxSolutions = valueCount[j];
                            }
                        }
                    }
                    if (count > 1) {
                        LivingLocation alive = new LivingLocation(bfa, live.index);
                        alive.mineCount = mines;
                        alive.count = count;
                        alive.minValue = minValue;
                        alive.maxValue = maxValue;
                        alive.maxSolutions = maxSolutions;
                        alive.zeroSolutions = valueCount[0];
                        living.Add(alive);
                    }

                }

                living.Sort();
                //Collections.sort(living);

                this.livingLocations = living;

            }

            public override int GetHashCode() {
                return position.GetHashCode();
            }

            public override bool Equals(Object o) {
                if (o is Node) {
                    return position.Equals(((Node)o).position);
                } else {
                    return false;
                }
            }

        }


        // start of main class

        private static readonly String INDENT = "................................................................................";

        //private static readonly BigDecimal ONE_HUNDRED = BigDecimal.valueOf(100);

        public int processCount = 0;

        private readonly SolverInfo solver;
        private readonly int maxSolutionSize;

        //private Node top;

        private readonly List<SolverTile> locations;         // the positions being analysed
        private readonly List<SolverTile> startLocations;    // the positions which will be considered for the first move

        private readonly SolutionTable allSolutions;

        //private readonly String scope;

        private Node currentNode;
        private SolverTile expectedMove;

        private readonly SortSolutions[] sorters;

        private int cacheHit = 0;
        public int cacheSize = 0;
        private int cacheWinningLines = 0;
        private bool allDead = false;   // this is true if all the locations are dead
        private bool tooMany = false;
        private bool completed = false;

        // some work areas to prevent having to instantiate many 1000's of copies of them 
        //private final boolean[] values = new boolean[9];
        private readonly int[] valueCount = new int[9];

        private Dictionary<Position, Node> cache = new Dictionary<Position, Node>(5000);

        public BruteForceAnalysis(SolverInfo solver, List<SolverTile> locations, int size, List<SolverTile> startLocations) {

            this.solver = solver;
            this.locations = locations;
            this.maxSolutionSize = size;

            //this.top = new Node();
            sorters = new SortSolutions[locations.Count];
            for (int i = 0; i < sorters.Length; i++) {
                sorters[i] = new SortSolutions(i);
            }

            this.allSolutions = new SolutionTable(this, size);

            this.startLocations = startLocations;

        }

        public void AddSolution(sbyte[] solution) {

            if (solution.Length != locations.Count) {
                throw new Exception("Solution does not have the correct number of locations");
            }

            if (allSolutions.GetSize() >= maxSolutionSize) {
                tooMany = true;
                return;
            }

            /*
            String text = "";
            for (int i=0; i < solution.length; i++) {
                text = text + solution[i] + " ";
            }
            solver.display(text);
            */

            allSolutions.AddSolution(solution);
 
        }

        public void process() {

            long start = DateTime.Now.Ticks;

            solver.Write("----- Brute Force Deep Analysis starting ----");
            solver.Write(allSolutions.GetSize() + " solutions in BruteForceAnalysis");

            // create the top node 
            Node top = buildTopNode(allSolutions);

            if (top.GetLivingLocations().Count == 0) {
                allDead = true;
            }

            int best = 0;

            foreach (LivingLocation move in top.GetLivingLocations()) {

                // check that the move is in the startLocation list
                if (startLocations != null) {
                    bool found = false;
                    foreach (SolverTile l in startLocations) {
                        if (locations[move.index].Equals(l)) {
                            found = true;
                            break;
                        }
                    }
                    if (!found) {  // if not then skip this move
                        solver.Write(move.index + " " + locations[move.index].AsText() + " is not a starting location");
                        continue;
                    }
                }

                int winningLines = top.GetWinningLines(move);  // calculate the number of winning lines if this move is played

                if (best < winningLines || (top.bestLiving != null && best == winningLines && top.bestLiving.mineCount < move.mineCount)) {
                    best = winningLines;
                    top.bestLiving = move;
                }

                double singleProb = (allSolutions.GetSize() - move.mineCount) / allSolutions.GetSize();

                if (move.pruned) {
                    solver.Write(move.index + " " + locations[move.index].AsText() + " is living with " + move.count + " possible values and probability " + singleProb + ", this location was pruned");
                } else {
                    solver.Write(move.index + " " + locations[move.index].AsText() + " is living with " + move.count + " possible values and probability " + singleProb + ", winning lines " + winningLines);
                }

            }

            top.winningLines = best;

            currentNode = top;

            if (processCount < SolverMain.BRUTE_FORCE_ANALYSIS_MAX_NODES) {
                this.completed = true;
                //if (solver.isShowProbabilityTree()) {
                //    solver.newLine("--------- Probability Tree dump start ---------");
                //    showTree(0, 0, top);
                //    solver.newLine("---------- Probability Tree dump end ----------");
                //}
            }


            // clear down the cache
            cache.Clear();

            long end = DateTime.Now.Ticks;

            solver.Write("Total nodes in cache = " + cacheSize + ", total cache hits = " + cacheHit + ", total winning lines saved = " + this.cacheWinningLines);
            solver.Write("process took " + (end - start) + " milliseconds and explored " + processCount + " nodes");
            solver.Write("----- Brute Force Deep Analysis finished ----");
        }

        /**
         * Builds a top of tree node based on the solutions provided
         */
        private Node buildTopNode(SolutionTable solutionTable) {

            Node result = new Node(this, locations.Count);

            result.startLocation = 0;
            result.endLocation = solutionTable.GetSize();

            List<LivingLocation> living = new List<LivingLocation>();

            for (short i = 0; i < locations.Count; i++) {
                int value;

                int[] valueCount = ResetValues();
                int mines = 0;
                int maxSolutions = 0;
                byte count = 0;
                sbyte minValue = 0;
                sbyte maxValue = 0;

                for (int j = 0; j < result.GetSolutionSize(); j++) {
                    if (solutionTable.Get(j)[i] != Cruncher.BOMB) {
                        value = solutionTable.Get(j)[i];
                        //values[value] = true;
                        valueCount[value]++;
                    } else {
                        mines++;
                    }
                }

                for (sbyte j = 0; j < valueCount.Length; j++) {
                    if (valueCount[j] > 0) {
                        if (count == 0) {
                            minValue = j;
                        }
                        maxValue = j;
                        count++;
                        if (maxSolutions < valueCount[j]) {
                            maxSolutions = valueCount[j];
                        }
                    }
                }
                if (count > 1) {
                    LivingLocation alive = new LivingLocation(this, i);
                    alive.mineCount = mines;
                    alive.count = count;
                    alive.minValue = minValue;
                    alive.maxValue = maxValue;
                    alive.maxSolutions = maxSolutions;
                    alive.zeroSolutions = valueCount[0];
                    living.Add(alive);
                } else {
                    solver.Write(locations[i].AsText() + " is dead with value " + minValue);
                }

            }

            living.Sort();
            //Collections.sort(living);

            result.livingLocations = living;

            return result;
        }

        public int[] ResetValues() {
            for (int i = 0; i < valueCount.Length; i++) {
                valueCount[i] = 0;
            }
            return valueCount;
        }

        public int GetSolutionCount() {
            return allSolutions.GetSize();
        }


        public int GetNodeCount() {
            return processCount;
        }

        public SolverAction GetNextMove() {

            LivingLocation bestLiving = getBestLocation(currentNode);

            if (bestLiving == null) {
                return null;
            }

            SolverTile loc = this.locations[bestLiving.index];

            //solver.display("first best move is " + loc.display());
            double prob = 1 - bestLiving.mineCount / currentNode.GetSolutionSize();

            while (!loc.IsHidden()) {
                int value = loc.GetValue();

                currentNode = bestLiving.children[value];
                bestLiving = getBestLocation(currentNode);
                if (bestLiving == null) {
                    return null;
                }
                prob = 1 - ((double) bestLiving.mineCount) / currentNode.GetSolutionSize();

                loc = this.locations[bestLiving.index];

            }

            solver.Write("mines = " + bestLiving.mineCount + " solutions = " + currentNode.GetSolutionSize());
            for (int i = 0; i < bestLiving.children.Length; i++) {
                if (bestLiving.children[i] == null) {
                    //solver.display("Value of " + i + " is not possible");
                    continue; //ignore this node but continue the loop
                }

                String probText;
                if (bestLiving.children[i].bestLiving == null) {
                    probText = (100 / (bestLiving.children[i].GetSolutionSize())) + "%";
                } else {
                    probText = bestLiving.children[i].GetProbability() * 100 + "%";
                }
                solver.Write("Value of " + i + " leaves " + bestLiving.children[i].GetSolutionSize() + " solutions and winning probability " + probText + " (work size " + bestLiving.children[i].work + ")");
            }

            //String text = " (solve " + (currentNode.GetProbability() * 100) + "%)";
            SolverAction action = new SolverAction(loc, ActionType.Clear, 0.5);

            expectedMove = loc;

            return action;

        }

        private LivingLocation getBestLocation(Node node) {
            return node.bestLiving;
        }

        private void ShowTree(int depth, int value, Node node) {

            String condition;
            if (depth == 0) {
                condition = node.GetSolutionSize() + " solutions remain";
            } else {
                condition = "When '" + value + "' ==> " + node.GetSolutionSize() + " solutions remain";
            }

            if (node.bestLiving == null) {
                String line1 = INDENT.Substring(0, depth * 3) + condition + " Solve chance " + node.GetProbability() * 100 + "%";
                Console.WriteLine(line1);
                return;
            }

            SolverTile loc = this.locations[node.bestLiving.index];

            double prob = 1 - node.bestLiving.mineCount / node.GetSolutionSize();


            String line = INDENT.Substring(0, depth * 3) + condition + " play " + loc.AsText() + " Survival chance " + prob * 100 + "%, Solve chance " + node.GetProbability() * 100 + "%";

            Console.WriteLine(line);

            //for (Node nextNode: node.bestLiving.children) {
            for (int val = 0; val < node.bestLiving.children.Length; val++) {
                Node nextNode = node.bestLiving.children[val];
                if (nextNode != null) {
                    ShowTree(depth + 1, val, nextNode);
                }

            }

        }

        public bool IsComplete() {
            return this.completed;
        }

        public SolverTile GetExpectedMove() {
            return expectedMove;
        }

        //private String percentage(double prob) {
        //    return Action.FORMAT_2DP.format(prob.multiply(ONE_HUNDRED));
        //}

        public bool GetAllDead() {
            return allDead;
        }

    }
}
