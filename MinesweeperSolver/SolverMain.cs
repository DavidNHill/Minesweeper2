using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using static MinesweeperControl.MinesweeperGame;
using static MinesweeperSolver.SolverInfo;

namespace MinesweeperSolver {



    class SolverMain {

        public static readonly BigInteger PARALLEL_MINIMUM = new BigInteger(10000);
        public static readonly BigInteger MAX_BRUTE_FORCE_ITERATIONS = new BigInteger(10000000);

        public const int BRUTE_FORCE_ANALYSIS_TREE_DEPTH = 20;
        public const int MAX_BFDA_SOLUTIONS = 400;
        public const int BRUTE_FORCE_ANALYSIS_MAX_NODES = 150000;
        public const bool PRUNE_BF_ANALYSIS = true;

        private static readonly Binomial binomial = new Binomial(500000, 500);

        public static SolverActionHeader FindActions(SolverInfo information) {

            long time1 = DateTime.Now.Ticks;

            List<SolverAction> actions ;
            
            if (information.GetGameStatus() == GameStatus.Lost) {
                information.Write("Game has been lost already - no valid moves");
                return new SolverActionHeader();
            } else if (information.GetGameStatus() == GameStatus.Won) {
                information.Write("Game has been won already - no valid moves");
                return new SolverActionHeader();
            } else if (information.GetGameStatus() == GameStatus.NotStarted) {
                information.Write("Game has not yet started - currently unable to provide help");
                return new SolverActionHeader();
            }

            // are we walking down a brute force deep analysis tree?
            BruteForceAnalysis lastBfa = information.GetBruteForceAnalysis();
            if (lastBfa != null) {   // yes
                SolverTile expectedMove = lastBfa.GetExpectedMove();
                if (expectedMove != null && expectedMove.IsHidden()) {    // the expected move wasn't played !
                    information.Write("The expected Brute Force Analysis move " + expectedMove.AsText() + " wasn't played");
                    information.SetBruteForceAnalysis(null);
                } else {
                    SolverAction move = lastBfa.GetNextMove();
                    if (move != null) {
                        information.Write("Next Brute Force Deep Analysis move is " + move.AsText());
                        actions = new List<SolverAction>(1);
                        actions.Add(move);
                        return BuildActionHeader(information, actions);
                    }
                }
            }


            actions = FindTrivialActions(information);

            long time2 = DateTime.Now.Ticks;

            information.Write("Finding Trivial Actions took " + (time2 - time1) + " ticks");

            // if we have some actions from the trivial search use them
            if (actions.Count > 0) {
                return BuildActionHeader(information, actions);
            }


            if (information.GetTilesLeft() == information.GetDeadTiles().Count) {
                information.Write("All tiles remaining are dead");

                // when all the tiles are dead return the first one (they are all equally good or bad)
                foreach (SolverTile guess in information.GetDeadTiles()) {
                    actions.Add(new SolverAction(guess, ActionType.Clear, 0.5));  // not all 0.5 safe though !!
                    return BuildActionHeader(information, actions);
                }
            }

            // get all the witnessed tiles

            List<SolverTile> witnesses = new List<SolverTile>(information.GetWitnesses());
            HashSet<SolverTile> witnessedSet = new HashSet<SolverTile>();

            foreach (SolverTile witness in witnesses) {

                foreach(SolverTile adjTile in information.GetAdjacentTiles(witness)) {

                    if (adjTile.IsHidden()) {
                        witnessedSet.Add(adjTile);
                    }
                }

            }

            List<SolverTile> witnessed = new List<SolverTile>(witnessedSet);
            int livingTilesLeft = information.GetTilesLeft();
            int livingMinesLeft = information.GetMinesLeft();
            int offEdgeTilesLeft = information.GetTilesLeft() - witnessed.Count;

            //information.Write("Excluded tiles " + information.GetExcludedTiles().Count + " out of " + information.GetTilesLeft());
            //information.Write("Excluded witnesses " + information.GetExcludedWitnesses().Count);
            //information.Write("Excluded mines " + information.GetExcludedMineCount() + " out of " + information.GetMinesLeft());

            // if there are no living mines but some living tiles then the living tiles can be cleared
            if (livingMinesLeft == 0 && livingTilesLeft > 0) {
                information.Write("There are no living mines left - all living tiles must be clearable");

                for (int x = 0; x < information.description.width; x++) {
                    for (int y = 0; y < information.description.height; y++) {
                        SolverTile tile = information.GetTile(x, y);
                        if (tile.IsHidden() && !tile.IsMine()) {
                            actions.Add(new SolverAction(tile, ActionType.Clear, 1));
                        }
                    }
                }
                return BuildActionHeader(information, actions);
            }


            SolutionCounter solutionCounter = new SolutionCounter(information, witnesses, witnessed, livingTilesLeft, livingMinesLeft);
            solutionCounter.Process();
            information.Write("Solution counter says " + solutionCounter.GetSolutionCount() + " solutions and " + solutionCounter.getClearCount() + " clears");


            //ProbabilityEngine pe = new ProbabilityEngine(information, witnesses, witnessed, information.GetTilesLeft(), information.GetMinesLeft());
            ProbabilityEngine pe = new ProbabilityEngine(information, witnesses, witnessed, livingTilesLeft, livingMinesLeft);

            pe.Process();

            long time3 = DateTime.Now.Ticks;

            information.Write("Probability Engine took " + (time3 - time2) + " ticks");

            // have we found any local clears which we can use
            List<SolverTile> localClears = pe.GetLocalClears();
            if (localClears.Count > 0) {
                foreach (SolverTile tile in localClears) {   // place each local clear into an action
                    actions.Add(new SolverAction(tile, ActionType.Clear, 1));
                }
                information.Write("The probability engine has found " + localClears.Count + " safe Local Clears");

                // add any mines to it
                List<SolverTile> minesFound = pe.GetMinesFound();
                foreach (SolverTile tile in minesFound) {   // place each mine found into an action
                    information.MineFound(tile);
                    actions.Add(new SolverAction(tile, ActionType.Flag, 0));
                }
                information.Write("The probability engine has found " + minesFound.Count + " mines");


                return BuildActionHeader(information, actions);
            }

            if (pe.GetBestEdgeProbability() == 1) {
                actions = pe.GetBestCandidates(1);
                information.Write("The probability engine has found " + actions.Count + " safe Clears");

                // add any mines to it
                List<SolverTile> minesFound = pe.GetMinesFound();
                foreach (SolverTile tile in minesFound) {   // place each mine found into an action
                    information.MineFound(tile);
                    actions.Add(new SolverAction(tile, ActionType.Flag, 0));
                }
                information.Write("The probability engine has found " + minesFound.Count + " mines");

                return BuildActionHeader(information, actions);
            }

            // dead edge (all tiles on the edge are dead and there is only one mine count value)
            if (pe.GetDeadEdge().Count != 0) {
                SolverTile tile = pe.GetDeadEdge()[0];
                information.Write("Probability engine has found a dead area, guessing at " + tile.AsText());
                double probability = Combination.DivideBigIntegerToDouble(BigInteger.One, pe.GetDeadEdgeSolutionCount(), 6);
                actions.Add(new SolverAction(tile, ActionType.Clear, probability));
                return BuildActionHeader(information, actions);
            }

            // Isolated edges found (all adjacent tiles are also on the edge and there is only one mine count value)
            if (pe.GetOutcome() == ProbabilityEngine.Outcome.ISOLATED_EDGE) {
                information.Write("Probability engine has found an isolated area");

                Cruncher cruncher = pe.getIsolatedEdgeCruncher();

                // determine all possible solutions
                cruncher.Crunch();

                // determine best way to solver them
                BruteForceAnalysis bfa = cruncher.GetBruteForceAnalysis();
                bfa.process();

                // if after trying to process the data we can't complete then abandon it
                if (!bfa.IsComplete()) {
                    information.Write("Abandoned the Brute Force Analysis after " + bfa.GetNodeCount() + " steps");
                    bfa = null;

                } else { // otherwise try and get the best long term move
                    information.Write("Built probability tree from " + bfa.GetSolutionCount() + " solutions in " + bfa.GetNodeCount() + " steps");
                    SolverAction move = bfa.GetNextMove();
                    if (move != null) {
                        information.SetBruteForceAnalysis(bfa);  // save the details so we can walk the tree
                        information.Write("Brute Force Analysis: " + move.AsText());
                        actions.Add(move);
                        return BuildActionHeader(information, actions);
                    } else if (bfa.GetAllDead()) {

                        SolverTile tile = cruncher.getTiles()[0];
                        information.Write("Brute Force Analysis has decided all tiles are dead on the Isolated Edge, guessing at " + tile.AsText());
                        double probability = Combination.DivideBigIntegerToDouble(BigInteger.One, bfa.GetSolutionCount(), 6);
                        actions.Add(new SolverAction(tile, ActionType.Clear, probability));
                        return BuildActionHeader(information, actions);

                    } else {
                        information.Write("Brute Force Analysis: no move found!");
                    }
                }
            }


            // after this point we know the probability engine didn't return any certain clears. But there are still some special cases when everything off edge is either clear or a mine

            // If there are tiles off the edge and they are definitely safe then clear them all, or mines then flag them
            if (offEdgeTilesLeft > 0 &&  (pe.GetOffEdgeProbability() == 1  || pe.GetOffEdgeProbability() == 0)) {
                information.Write("Looking for the certain moves off the edge found by the probability engine");
                bool clear;
                 if (pe.GetOffEdgeProbability() == 1) {
                    information.Write("All off edge tiles are clear");
                    clear = true;
                } else {
                    information.Write("All off edge tiles are mines");
                    clear = false;
                }

                for (int x=0; x < information.description.width; x++) {
                    for (int y = 0; y < information.description.height; y++) {

                        SolverTile tile = information.GetTile(x, y);

                        if (tile.IsHidden() && !witnessedSet.Contains(tile)) {
                            if (clear) {
                                information.Write(tile.AsText() + " is clear");
                                actions.Add(new SolverAction(tile, ActionType.Clear, 1));
                            } else {
                                information.Write(tile.AsText() + " is mine");
                                information.MineFound(tile);
                                actions.Add(new SolverAction(tile, ActionType.Flag, 0));
                            }
                            
                        }
                    }
                }

                if (actions.Count > 0) {

                    // add any mines to it
                    List<SolverTile> minesFound = pe.GetMinesFound();
                    foreach (SolverTile tile in minesFound) {   // place each mine found into an action
                        information.MineFound(tile);
                        actions.Add(new SolverAction(tile, ActionType.Flag, 0));
                    }
                    information.Write("The probability engine has found " + minesFound.Count + " mines");

                    return BuildActionHeader(information, actions);
                } else {
                    Console.WriteLine("No Actions found!");
                }

            }


            // these are guesses
            List<SolverAction> guesses = pe.GetBestCandidates(1);

            // we know the Probability Engine completed so hold onto the information for the gui 
            information.SetProbabilityEngine(pe);

            // if there aren't many possible solutions then do a brute force search
            if (pe.GetSolutionCount() <= MAX_BFDA_SOLUTIONS) {

                //if (minesFound.Count > 0) {
                //    information.Write("Not doing a brute force analysis because we found some mines using the probability engine");
                //    return BuildActionHeader(information, actions);
                //}

                // find a set of independent witnesses we can use as the base of the iteration
                pe.GenerateIndependentWitnesses();

                BigInteger expectedIterations = pe.GetIndependentIterations() * SolverMain.Calculate(livingMinesLeft - pe.GetIndependentMines(), livingTilesLeft - pe.GetIndependentTiles());

                information.Write("Expected Brute Force iterations " + expectedIterations);

                // do the brute force if there are not too many iterations
                if (expectedIterations < MAX_BRUTE_FORCE_ITERATIONS) {

                    List<SolverTile> allCoveredTiles = new List<SolverTile>();

                    for (int x = 0; x < information.description.width; x++) {
                        for (int y = 0; y < information.description.height; y++) {
                            SolverTile tile = information.GetTile(x, y);
                            if (tile.IsHidden() && !tile.IsMine()) {
                                allCoveredTiles.Add(tile);
                            }
                        }
                    }

                    WitnessWebIterator[] iterators = BuildParallelIterators(information, pe, allCoveredTiles, expectedIterations);
                    BruteForceAnalysis bfa = Cruncher.PerformBruteForce(information, iterators, pe.GetDependentWitnesses());

                    bfa.process();

                    // if after trying to process the data we can't complete then abandon it
                    if (!bfa.IsComplete()) {
                        information.Write("Abandoned the Brute Force Analysis after " + bfa.GetNodeCount() + " steps");
                        bfa = null;

                    } else { // otherwise try and get the best long term move
                        information.Write("Built probability tree from " + bfa.GetSolutionCount() + " solutions in " + bfa.GetNodeCount() + " steps");
                        SolverAction move = bfa.GetNextMove();
                        if (move != null) {
                            information.SetBruteForceAnalysis(bfa);  // save the details so we can walk the tree
                            information.Write("Brute Force Analysis: " + move.AsText());
                            actions.Add(move);
                            return BuildActionHeader(information, actions);
                        } else {
                            information.Write("Brute Force Analysis: no move found!");
                        }
                    }
                } else {
                    information.Write("Too many iterations, Brute Force not atempted");
                }

            }


            if (guesses.Count == 0) {  // find an off edge guess
                if (offEdgeTilesLeft > 0) {
                    information.Write("getting an off edge guess");
                    SolverTile tile = OffEdgeGuess(information, witnessedSet);
                    SolverAction action = new SolverAction(tile, ActionType.Clear, pe.GetOffEdgeProbability());
                    information.Write(action.AsText());
                    actions.Add(action);
                } else {
                    if (information.GetDeadTiles().Count > 0) {
                        information.Write("Finding a dead tile to guess");
                        SolverTile tile = null;
                        foreach (SolverTile deadTile in information.GetDeadTiles()) {   // get the first dead tile
                            tile = deadTile;
                            break;
                        }
                        SolverAction action = new SolverAction(tile, ActionType.Clear, 0.5);  // probability may not be 0.5  
                        actions.Add(action);
                    }
                }


            } else if (guesses.Count > 1) {  // if we have more than 1 guess then do some tie break logic
                information.Write("Doing a tie break for " + guesses.Count + " actions");
                actions = DoTieBreak(guesses);

            } else {
                actions = guesses;
            }

            return BuildActionHeader(information, actions);

        }

        private static SolverActionHeader BuildActionHeader(SolverInfo information, List<SolverAction> actions) {

            HashSet<SolverTile> dead = information.GetDeadTiles();

            // add any dead tiles to the list of actions
            List<SolverAction> deadList = new List<SolverAction>();
            foreach (SolverTile dt in dead) {
                deadList.Add(new SolverAction(dt, ActionType.Dead, 1));
            }

            return new SolverActionHeader(actions, deadList);

        }

        private static List<SolverAction> FindTrivialActions(SolverInfo information) {

            List<SolverAction> actions = new List<SolverAction>();

            // provide actions for known mines which aren't flagged
            foreach (SolverTile tile in information.GetKnownMines()) {
                if (!tile.IsFlagged()) {
                    actions.Add(new SolverAction(tile, ActionType.Flag, 0)); // request it is flagged
                }
            }

            actions.AddRange(ScanForTrivialActions(information, information.GetWitnesses()));

            //actions.AddRange(ScanForTrivialActions(information, information.GetExcludedWitnesses()));

            information.Write("Found " + actions.Count + " trivial actions");

            return actions;

        }

        private static List<SolverAction> ScanForTrivialActions(SolverInfo information, HashSet<SolverTile> set) {

            List<SolverAction> actions = new List<SolverAction>(); ;

            HashSet<SolverTile> alreadyProcessed = new HashSet<SolverTile>();

            foreach (SolverTile tile in set) {

                AdjacentInfo adjInfo = information.AdjacentTileInfo(tile);

                if (tile.GetValue() == adjInfo.mines) {  // if we have the correct number of mines then the remaining hidden tiles can be cleared
                    foreach (SolverTile adjTile in information.GetAdjacentTiles(tile)) {
                        if (!alreadyProcessed.Contains(adjTile) && adjTile.IsHidden() && !adjTile.IsMine()) {
                            alreadyProcessed.Add(adjTile);   // avoid marking as cleared more than once
                            //Utility.Write(adjTile.AsText() + " can be cleared");
                            actions.Add(new SolverAction(adjTile, ActionType.Clear, 1));
                        }
                    }
                }
                if (tile.GetValue() == adjInfo.mines + adjInfo.hidden) {  // If Hidden + Mines we already know about = tile value then the rest must be mines
                    foreach (SolverTile adjTile in information.GetAdjacentTiles(tile)) {
                        if (!alreadyProcessed.Contains(adjTile) && adjTile.IsHidden() && !adjTile.IsMine()) {
                            alreadyProcessed.Add(adjTile);   // avoid marking as a mine more than once
                            //Utility.Write(adjTile.AsText() + " is a mine");
                            if (!information.MineFound(adjTile)) {
                                actions.Add(new SolverAction(adjTile, ActionType.Flag, 0)); // and request it is flagged
                            }
                        }
                    }
                }

                // 2 mines to find and only 3 tiles to put them
                if (tile.GetValue() - adjInfo.mines == 2 && adjInfo.hidden == 3) {
                    foreach (SolverTile adjTile in information.GetAdjacentTiles(tile)) {
                        if (!adjTile.IsHidden() && !adjTile.IsMine() && !adjTile.IsExhausted()) {

                            AdjacentInfo tileAdjInfo = information.AdjacentTileInfo(adjTile);

                            if (adjTile.GetValue() - tileAdjInfo.mines == 1) {   // an adjacent tile only needs to find one mine

                                int notAdjacentCount = 0;
                                SolverTile notAdjTile = null;
                                foreach (SolverTile adjTile2 in information.GetAdjacentTiles(tile)) {
                                    if (adjTile2.IsHidden() && !adjTile2.IsAdjacent(adjTile)) {
                                        notAdjacentCount++;
                                        notAdjTile = adjTile2;
                                    }
                                }

                                if (notAdjacentCount == 1 && !alreadyProcessed.Contains(notAdjTile)) {     // we share all but one tile, so that one tile must contain the extra mine
                                    alreadyProcessed.Add(notAdjTile);   // avoid marking as a mine more than once
                                    if (!information.MineFound(notAdjTile)) {
                                        actions.Add(new SolverAction(notAdjTile, ActionType.Flag, 0)); // and request it is flagged
                                        break;   // restrict to finding one mine at a time  (the action of marking the mine throws any further analysis out)
                                    }
                                }

                            }
                        }
                    }
                }

                // Subtraction method
                //continue;  // skip this bit
                foreach (SolverTile adjTile in information.GetAdjacentTiles(tile)) {

                    if (!adjTile.IsHidden() && !adjTile.IsMine() && !adjTile.IsExhausted()) {

                        AdjacentInfo tileAdjInfo = information.AdjacentTileInfo(adjTile);

                        if (adjTile.GetValue() - tileAdjInfo.mines == tile.GetValue() - adjInfo.mines) {  // if the adjacent tile is revealed and shares the same number of mines to find
                                                                                                          // If all the adjacent tiles adjacent tiles are also adjacent to the original tile
                            bool allAdjacent = true;
                            foreach (SolverTile adjTile2 in information.GetAdjacentTiles(adjTile)) {
                                if (adjTile2.IsHidden() && !adjTile2.IsAdjacent(tile)) {
                                    allAdjacent = false;
                                    break;
                                }
                            }

                            if (allAdjacent) {
                                //information.Write(tile.AsText() + " can be subtracted by " + adjTile.AsText());
                                foreach (SolverTile adjTile2 in information.GetAdjacentTiles(tile)) {
                                    if (adjTile2.IsHidden() && !adjTile2.IsAdjacent(adjTile) && !alreadyProcessed.Contains(adjTile2)) {
                                        alreadyProcessed.Add(adjTile2);   // avoid marking as a mine more than once
                                        actions.Add(new SolverAction(adjTile2, ActionType.Clear, 1));
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return actions;
        }

        private static SolverTile OffEdgeGuess(SolverInfo information, HashSet<SolverTile> witnessedSet) {

            // see if the corners are available
            SolverTile bestGuess = information.GetTile(0, 0);
            if (bestGuess.IsHidden() && !witnessedSet.Contains(bestGuess)) {
                return bestGuess;
            }
            bestGuess = information.GetTile(0, information.description.height - 1);
            if (bestGuess.IsHidden() && !witnessedSet.Contains(bestGuess)) {
                return bestGuess;
            }
            bestGuess = information.GetTile(information.description.width - 1, 0);
            if (bestGuess.IsHidden() && !witnessedSet.Contains(bestGuess)) {
                return bestGuess;
            }
            bestGuess = information.GetTile(information.description.width - 1, information.description.height - 1);
            if (bestGuess.IsHidden() && !witnessedSet.Contains(bestGuess)) {
                return bestGuess;
            }

            bestGuess = null;
            int bestGuessCount = 9;

            for (int x = 0; x < information.description.width; x++) {
                for (int y = 0; y < information.description.height; y++) {

                    SolverTile tile = information.GetTile(x, y);

                    if (tile.IsHidden() && !witnessedSet.Contains(tile)) {
                        AdjacentInfo adjInfo = information.AdjacentTileInfo(tile);
                        if (adjInfo.hidden < bestGuessCount) {
                            bestGuess = tile;
                            bestGuessCount = adjInfo.hidden;
                        }
                    }
                }
            }

            return bestGuess;

        }


        // do tie break logic
        private static List<SolverAction> DoTieBreak(List<SolverAction> actions) {

            List<SolverAction> result = new List<SolverAction>();

            // find and return a not dead tile
            foreach (SolverAction sa in actions) {
                if (!sa.isDead) {
                    result.Add(sa);
                    return result;
                }
            }

            // otherwise return the first one
            result.Add(actions[0]);

            return result;

        }

        // break a witness web search into a number of non-overlapping iterators
        private static WitnessWebIterator[] BuildParallelIterators(SolverInfo information, ProbabilityEngine pe, List<SolverTile> allCovered, BigInteger expectedIterations) {

            information.Write("Building parallel iterators");

            //information.Write("Non independent iterations = " + pe.GetNonIndependentIterations(mines));

            int minesLeft = information.GetMinesLeft();

            //the number of witnesses available
            int totalWitnesses = pe.GetIndependentWitnesses().Count + pe.GetDependentWitnesses().Count;

            // if there is only one cog then we can't lock it,so send back a single iterator
            if (pe.GetIndependentWitnesses().Count == 1 && pe.GetIndependentMines() >= minesLeft || expectedIterations.CompareTo(PARALLEL_MINIMUM) < 0 || totalWitnesses == 0) {
                information.Write("Only a single iterator will be used");
                WitnessWebIterator[] one = new WitnessWebIterator[1];
                one[0] = new WitnessWebIterator(information, pe.GetIndependentWitnesses(), pe.GetDependentWitnesses(), allCovered, minesLeft, information.GetTilesLeft(), -1);
                return one;
            }

            int witMines = pe.GetIndependentWitnesses()[0].GetMinesToFind();
            int squares = pe.GetIndependentWitnesses()[0].GetAdjacentTiles().Count;

            BigInteger bigIterations = Calculate(witMines, squares);

            int iter = (int) bigIterations;

            information.Write("The first cog has " + iter + " iterations, so parallel processing is possible");

            WitnessWebIterator[] result = new WitnessWebIterator[iter];

            for (int i = 0; i < iter; i++) {
                // create a iterator with a lock first got at position i
                result[i] = new WitnessWebIterator(information, pe.GetIndependentWitnesses(), pe.GetDependentWitnesses(), allCovered, minesLeft, information.GetTilesLeft(), i);
            }

            return result;

        }


        public static BigInteger Calculate(int mines, int squares) {
            return binomial.Generate(mines, squares);
        }

        public static void Initialise() {

        }
    }
}
