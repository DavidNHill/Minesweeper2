using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace MinesweeperSolver {
/**
 *  Performs a brute force search on the provided squares using the iterator 
 * 
 */
public class Cruncher {

        public const sbyte BOMB = -10;

        readonly private SolverInfo information;
        private readonly WitnessWebIterator iterator;
        readonly private List<SolverTile> tiles;
        readonly private List<BoxWitness> witnesses;
        readonly private sbyte[] currentFlagsTiles;
        readonly private sbyte[] currentFlagsWitnesses;

        private int candidates = 0;  // number of samples which satisfy the current board state
        private readonly BruteForceAnalysis bfa;

        public Cruncher(SolverInfo information, WitnessWebIterator iterator, List<BoxWitness> witnesses, BruteForceAnalysis bfa) {

            this.information = information;
            this.iterator = iterator;   // the iterator
            this.tiles = iterator.getTiles();  // the tiles the iterator is iterating over
            this.witnesses = witnesses;  // the dependent witnesses (class BoxWitness) which need to be checked to see if they are satisfied

            this.bfa = bfa;

            // determine how many found mines are currently next to each tile
            this.currentFlagsTiles = new sbyte[this.tiles.Count];
            for (int i = 0; i < this.tiles.Count; i++) {
                this.currentFlagsTiles[i] = (sbyte) this.information.AdjacentTileInfo(this.tiles[i]).mines;
            }


            // determine how many found mines are currently next to each witness
            this.currentFlagsWitnesses = new sbyte[this.witnesses.Count];
            for (int i = 0; i < this.witnesses.Count; i++) {
                this.currentFlagsWitnesses[i] = (sbyte) this.information.AdjacentTileInfo(this.witnesses[i].GetTile()).mines;
            }

        }

        public static BruteForceAnalysis PerformBruteForce(SolverInfo information, WitnessWebIterator[] iterators, List<BoxWitness> witnesses) {

            BruteForceAnalysis bfa = new BruteForceAnalysis(information, iterators[0].getTiles(), SolverMain.MAX_BFDA_SOLUTIONS, null);

            Cruncher[] crunchers = new Cruncher[iterators.Length];
            Task[] tasks = new Task[iterators.Length];

            for (int i=0; i < iterators.Length; i++) {

                crunchers[i] = new Cruncher(information, iterators[i], witnesses, bfa);
                Cruncher cruncher = crunchers[i];

                tasks[i] = Task.Factory.StartNew(() => { cruncher.Crunch(); });
            }
            Task.WaitAll(tasks);
 
            int solutions = 0;
            int iterations = 0;
            for (int i = 0; i < iterators.Length; i++) {
                solutions = solutions + crunchers[i].GetSolutionsFound();
                iterations = iterations + iterators[i].GetIterations();
            }

            information.Write("Solutions found by brute force " + solutions + " after " + iterations + " iterations");
 
            return bfa;

        }


        public void Crunch() {

            int[] sample = this.iterator.GetSample();

            while (sample != null) {

                if (this.CheckSample(sample)) {
                    candidates++;
                }

                sample = this.iterator.GetSample();

            }

        }

        public List<SolverTile> getTiles() {
            return iterator.getTiles();
        }

        // this checks whether the positions of the mines are a valid candidate solution
        private bool CheckSample(int[] sample) {

            // get the tiles which are mines in this sample
            SolverTile[] mine = new SolverTile[sample.Length];
            for (int i = 0; i < sample.Length; i++) {
                mine[i] = this.tiles[sample[i]];
            }

            for (int i = 0; i < this.witnesses.Count; i++) {

                int flags1 = this.currentFlagsWitnesses[i];
                int flags2 = 0;

                // count how many candidate mines are next to this witness
                for (int j = 0; j < mine.Length; j++) {
                    if (mine[j].IsAdjacent(this.witnesses[i].GetTile())) {
                        flags2++;
                    }
                }

                int flags3 = this.witnesses[i].GetTile().GetValue();  // number of flags indicated on the tile

                if (flags3 != flags1 + flags2) {
                    //Console.WriteLine("Failed");
                    return false;
                }
            }

            //if it is a good solution then calculate the distribution if required

            //Console.WriteLine("Solution found");

            sbyte[] solution = new sbyte[this.tiles.Count];

            for (int i = 0; i < this.tiles.Count; i++) {

                bool isMine = false;
                for (int j = 0; j < sample.Length; j++) {
                    if (i == sample[j]) {
                        isMine = true;
                        break;
                    }
                }

                // if we are a mine then it doesn't matter how many mines surround us
                if (!isMine) {
                    sbyte flags2 = this.currentFlagsTiles[i];
                    // count how many candidate mines are next to this square
                    for (int j = 0; j < mine.Length; j++) {
                        if (mine[j].IsAdjacent(this.tiles[i])) {
                            flags2++;
                        }
                    }
                    solution[i] = flags2;
                } else {
                    solution[i] = BOMB;
                }

            }

            bfa.AddSolution(solution);
 
            /*
            string output = "";
            for (int i = 0; i < mine.length; i++) {
                output = output + mine[i].asText();
            }
            console.log(output);
            */

            return true;

        }

        public BruteForceAnalysis GetBruteForceAnalysis() {
            return this.bfa;
        }

        public int GetSolutionsFound() {
            return this.candidates;
        }
    }


    // create an iterator which is like a set of rotating wheels
    public class WitnessWebIterator {

        private int[] sample;
        private SequentialIterator[] cogs;
        private int[] squareOffset;
        private int[] mineOffset;
        private List<SolverTile> tiles;

        private int iterationsDone = 0;

        readonly private int top;
        readonly private int bottom;

        private bool done = false;

        /*
        // if rotation is -1 then this does all the possible iterations
        // if rotation is not - 1 then this locks the first 'cog' in that position and iterates the remaining cogs.  This allows parallel processing based on the position of the first 'cog'
        public WitnessWebIterator(ProbabilityEngine pe, List<SolverTile> allCoveredTiles, int rotation) {

            this.tiles = new List<SolverTile>();  // list of tiles being iterated over

            this.cogs = new SequentialIterator[pe.GetIndependentWitnesses().Count + 1]; // array of cogs
            this.squareOffset = new int[pe.GetIndependentWitnesses().Count + 1];  // int array
            this.mineOffset = new int[pe.GetIndependentWitnesses().Count + 1];   // int array

            this.iterationsDone = 0;

            this.done = false;

            //this.probabilityEngine = pe;

            // if we are setting the position of the top cog then it can't ever change
            if (rotation == -1) {
                this.bottom = 0;
            } else {
                this.bottom = 1;
            }

            List<SolverTile> loc = new List<SolverTile>();  // array of locations

            List<BoxWitness> indWitnesses = pe.GetIndependentWitnesses();

            int cogi = 0;
            int indSquares = 0;
            int indMines = 0;

            // create an array of locations in the order of independent witnesses
            foreach (BoxWitness w in indWitnesses) {

                this.squareOffset[cogi] = indSquares;
                this.mineOffset[cogi] = indMines;
                this.cogs[cogi] = new SequentialIterator(w.GetMinesToFind(), w.GetAdjacentTiles().Count);
                cogi++;

                indSquares = indSquares + w.GetAdjacentTiles().Count;
                indMines = indMines + w.GetMinesToFind();

                loc.AddRange(w.GetAdjacentTiles());

            }

            //System.out.println("Mines left = " + (mines - indMines));
            //System.out.println("Squrs left = " + (web.getSquares().length - indSquares));

            // the last cog has the remaining squares and mines

            //add the rest of the locations
            for (int i = 0; i < allCoveredTiles.Count; i++) {

                SolverTile l = allCoveredTiles[i];
                bool skip = false;
                for (int j = 0; j < loc.Count; j++) {

                    SolverTile m = loc[j];

                    if (l.IsEqual(m)) {
                        skip = true;
                        break;
                    }
                }
                if (!skip) {
                    loc.Add(l);
                }
            }

            this.tiles = loc;

            SolverInfo information = pe.GetSolverInfo();

            int minesLeft = information.GetMinesLeft() - information.GetExcludedMineCount();
            int tilesLeft = information.GetTilesLeft() - information.GetExcludedTiles().Count;

            information.Write("Mines left " + minesLeft);
            information.Write("Independent Mines " + indMines);
            information.Write("Tiles left " + tilesLeft);
            information.Write("Independent tiles " + indSquares);


            // if there are more mines left then squares then no solution is possible
            // if there are not enough mines to satisfy the minimum we know are needed
            if (minesLeft - indMines > tilesLeft - indSquares
                || indMines > minesLeft) {
                this.done = true;
                this.top = 0;
                Console.WriteLine("Nothing to do in this iterator");
                return;
            }

            // if there are no mines left then no need for a cog
            if (minesLeft > indMines) {
                this.squareOffset[cogi] = indSquares;
                this.mineOffset[cogi] = indMines;
                this.cogs[cogi] = new SequentialIterator(minesLeft - indMines, tilesLeft - indSquares);
                this.top = cogi;
            } else {
                top = cogi - 1;
            }

            //this.top = this.cogs.Length - 1;

            this.sample = new int[minesLeft];  // make the sample array the size of the number of mines

            // if we are locking and rotating the top cog then do it
            if (rotation != -1) {
                for (var i = 0; i < rotation; i++) {
                    this.cogs[0].GetNextSample();
                }
            }

            // now set up the initial sample position
            for (int i = 0; i < this.top; i++) {
                int[] s = this.cogs[i].GetNextSample();
                for (int j = 0; j < s.Length; j++) {
                    this.sample[this.mineOffset[i] + j] = this.squareOffset[i] + s[j];
                }
            }
        }
        */

        // if rotation is -1 then this does all the possible iterations
        // if rotation is not - 1 then this locks the first 'cog' in that position and iterates the remaining cogs.  This allows parallel processing based on the position of the first 'cog'
        public WitnessWebIterator(SolverInfo information, List<BoxWitness> independentWitnesses, List<BoxWitness> depdendentWitnesses
            , List<SolverTile> allCoveredTiles, int minesLeft, int tilesLeft, int rotation) {

            this.tiles = new List<SolverTile>();  // list of tiles being iterated over

            int cogs;
            if (independentWitnesses == null) {
                cogs = 1;
            } else {
                cogs = independentWitnesses.Count + 1;
            }
 
            this.cogs = new SequentialIterator[cogs]; // array of cogs
            this.squareOffset = new int[cogs];  // int array
            this.mineOffset = new int[cogs];   // int array

            this.iterationsDone = 0;

            this.done = false;

            //this.probabilityEngine = pe;

            // if we are setting the position of the top cog then it can't ever change
            if (rotation == -1) {
                this.bottom = 0;
            } else {
                this.bottom = 1;
            }

            List<SolverTile> loc = new List<SolverTile>();  // array of locations

            int cogi = 0;
            int indSquares = 0;
            int indMines = 0;

            // create an array of locations in the order of independent witnesses
            if (independentWitnesses != null) {
                foreach (BoxWitness w in independentWitnesses) {

                    this.squareOffset[cogi] = indSquares;
                    this.mineOffset[cogi] = indMines;
                    this.cogs[cogi] = new SequentialIterator(w.GetMinesToFind(), w.GetAdjacentTiles().Count);
                    cogi++;

                    indSquares = indSquares + w.GetAdjacentTiles().Count;
                    indMines = indMines + w.GetMinesToFind();

                    loc.AddRange(w.GetAdjacentTiles());

                }
            }
 

            //System.out.println("Mines left = " + (mines - indMines));
            //System.out.println("Squrs left = " + (web.getSquares().length - indSquares));

            // the last cog has the remaining squares and mines

            //add the rest of the locations
            for (int i = 0; i < allCoveredTiles.Count; i++) {

                SolverTile l = allCoveredTiles[i];
                bool skip = false;
                for (int j = 0; j < loc.Count; j++) {

                    SolverTile m = loc[j];

                    if (l.IsEqual(m)) {
                        skip = true;
                        break;
                    }
                }
                if (!skip) {
                    loc.Add(l);
                }
            }

            this.tiles = loc;

            information.Write("Mines left " + minesLeft);
            information.Write("Independent Mines " + indMines);
            information.Write("Tiles left " + tilesLeft);
            information.Write("Independent tiles " + indSquares);


            // if there are more mines left then squares then no solution is possible
            // if there are not enough mines to satisfy the minimum we know are needed
            if (minesLeft - indMines > tilesLeft - indSquares
                || indMines > minesLeft) {
                this.done = true;
                this.top = 0;
                Console.WriteLine("Nothing to do in this iterator");
                return;
            }

            // if there are no mines left then no need for a cog
            if (minesLeft > indMines) {
                this.squareOffset[cogi] = indSquares;
                this.mineOffset[cogi] = indMines;
                this.cogs[cogi] = new SequentialIterator(minesLeft - indMines, tilesLeft - indSquares);
                this.top = cogi;
            } else {
                top = cogi - 1;
            }

            //this.top = this.cogs.Length - 1;

            this.sample = new int[minesLeft];  // make the sample array the size of the number of mines

            // if we are locking and rotating the top cog then do it
            if (rotation != -1) {
                for (var i = 0; i < rotation; i++) {
                    this.cogs[0].GetNextSample();
                }
            }

            // now set up the initial sample position
            for (int i = 0; i < this.top; i++) {
                int[] s = this.cogs[i].GetNextSample();
                for (int j = 0; j < s.Length; j++) {
                    this.sample[this.mineOffset[i] + j] = this.squareOffset[i] + s[j];
                }
            }
        }

        public int[] GetSample() {


            if (this.done) {
                Console.WriteLine("**** attempting to iterator when already completed ****");
                return null;
            }
            int index = this.top;

            int[] s = this.cogs[index].GetNextSample();

            while (s == null && index != this.bottom) {
                index--;
                s = this.cogs[index].GetNextSample();
            }

            if (index == this.bottom && s == null) {
                this.done = true;
                return null;
            }

            for (int j = 0; j < s.Length; j++) {
                this.sample[this.mineOffset[index] + j] = this.squareOffset[index] + s[j];
            }
            index++;
            while (index <= this.top) {
                this.cogs[index] = new SequentialIterator(this.cogs[index].GetNumberBalls(), this.cogs[index].GetNumberHoles());
                s = this.cogs[index].GetNextSample();
                for (int j = 0; j < s.Length; j++) {
                    this.sample[this.mineOffset[index] + j] = this.squareOffset[index] + s[j];
                }
                index++;
            }

            /*
            String output = "";
            for (int j = 0; j < sample.Length; j++) {
                output = output + this.sample[j] + " ";
            }
            Console.WriteLine(output);
            */

            this.iterationsDone++;

            return this.sample;

        }

        public List<SolverTile> getTiles() {
            return this.tiles;
        }

 
        public int GetIterations() {
            return iterationsDone;
        }

        /*
         // if the location is a Independent witness then we know it will always
         // have exactly the correct amount of mines around it since that is what
         // this iterator does
         public bool WitnessAlwaysSatisfied(SolverTile location) {

             for (var i = 0; i < this.probabilityEngine.independentWitness.length; i++) {
                 if (this.probabilityEngine.independentWitness[i].equals(location)) {
                     return true;
                 }
             }

             return false;

         }
        */

    }


    public class SequentialIterator {

        readonly private int[] sample;
        readonly private int numberHoles;
        readonly private int numberBalls;

        private bool more;
        private int index;

        // a sequential iterator that puts n-balls in m-holes once in each possible way
        public SequentialIterator(int n, int m) {

            this.numberHoles = m;
            this.numberBalls = n;

            this.sample = new int[n];

            this.more = true;

            this.index = n - 1;

            for (int i = 0; i < n; i++) {
                this.sample[i] = i;
            }

            // reduce the iterator by 1, since the first getSample() will increase it
            // by 1 again
            this.sample[this.index]--;

            //Console.WriteLine("Sequential Iterator has " + this.numberBalls + " mines and " + this.numberHoles + " squares");

        }

        public int[] GetNextSample() {

            if (!this.more) {
                Console.WriteLine("****  Trying to iterate after the end ****");
                return null;
            }

            this.index = this.numberBalls - 1;

            // add on one to the iterator
            this.sample[this.index]++;

            // if we have rolled off the end then move backwards until we can fit
            // the next iteration
            while (this.sample[this.index] >= this.numberHoles - this.numberBalls + 1 + this.index) {
                if (this.index == 0) {
                    this.more = false;
                    return null;
                } else {
                    this.index--;
                    this.sample[this.index]++;
                }
            }

            // roll forward 
            while (this.index != this.numberBalls - 1) {
                this.index++;
                this.sample[this.index] = this.sample[this.index - 1] + 1;
            }

            return this.sample;

        }

        public int GetNumberBalls() {
            return this.numberBalls;
        }

        public int GetNumberHoles() {
            return this.numberHoles;
        }

    }
}
