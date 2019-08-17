using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using static MinesweeperControl.MinesweeperGame;
using static MinesweeperSolver.SolverInfo;

namespace MinesweeperSolver {



    class SolverMain {

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
                    actions.Add(new SolverAction(guess, ActionType.Clear, 0.5));
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
            int livingTilesLeft = information.GetTilesLeft() - information.GetExcludedTiles().Count;
            int livingMinesLeft = information.GetMinesLeft() - information.GetExcludedMineCount();
            int offEdgeTilesLeft = information.GetTilesLeft() - witnessed.Count - information.GetExcludedTiles().Count;

            information.Write("Excluded tiles " + information.GetExcludedTiles().Count);
            information.Write("Excluded witnesses " + information.GetExcludedWitnesses().Count);
            information.Write("Excluded mines " + information.GetExcludedMineCount());

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
                information.Write("The probability engine has found " + localClears.Count + " safe clears");
                return BuildActionHeader(information, actions);
            }

            // after this point we know the probability engine didn't return any certain clears. But there are still some special cases when everything off edge is either clear or a mine

            // If there are tiles off the edge and they are definitely safe then clear them all, or mines then flag them
            if (offEdgeTilesLeft > 0 &&  (pe.GetOffEdgeProbability() == 1  || pe.GetOffEdgeProbability() == 0)) {
                bool clear;
                 if (pe.GetOffEdgeProbability() == 1) {
                    //Console.WriteLine("All off edge tiles are clear");
                    clear = true;
                } else {
                    //Console.WriteLine("All off edge tiles are mines");
                    clear = false;
                }

                for (int x=0; x < information.description.width; x++) {
                    for (int y = 0; y < information.description.height; y++) {

                        SolverTile tile = information.GetTile(x, y);

                        if (tile.IsHidden() && !witnessedSet.Contains(tile) && !tile.IsExcluded()) {
                            if (clear) {
                                actions.Add(new SolverAction(tile, ActionType.Clear, 1));
                            } else {
                                information.MineFound(tile);
                                actions.Add(new SolverAction(tile, ActionType.Flag, 0));
                            }
                            
                        }
                    }
                }

                if (actions.Count > 0) {
                    //information.Write("The solver has determined all off edge tiles must be safe");
                    return BuildActionHeader(information, actions);
                } else {
                    //Console.WriteLine("No Actions found!");
                }

            }


            // these are guesses
            List<SolverAction> guesses = pe.GetBestCandidates(1);

            // we know the Probability Engine completed so hold onto the information for the gui 
            information.SetProbabilityEngine(pe);

            if (guesses.Count == 0) {  // find an off edge guess
                if (offEdgeTilesLeft > 0) {
                    information.Write("getting an off edge guess");
                    SolverTile tile = OffEdgeGuess(information, witnessedSet);
                    SolverAction action = new SolverAction(tile, ActionType.Clear, pe.GetOffEdgeProbability());
                    actions.Add(action);
                } else {
                    if (information.GetDeadTiles().Count > 0) {
                        information.Write("Finding a dead tile to guess");
                        SolverTile tile = null;
                        foreach (SolverTile deadTile in information.GetDeadTiles()) {   // get the first dead tile
                            tile = deadTile;
                            break;
                        }
                        SolverAction action = new SolverAction(tile, ActionType.Clear, pe.GetProbability(tile));
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

            actions.AddRange(ScanForTrivialActions(information, information.GetExcludedWitnesses()));

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
            if (bestGuess.IsHidden() && !witnessedSet.Contains(bestGuess) && !bestGuess.IsExcluded()) {
                return bestGuess;
            }
            bestGuess = information.GetTile(0, information.description.height - 1);
            if (bestGuess.IsHidden() && !witnessedSet.Contains(bestGuess) && !bestGuess.IsExcluded()) {
                return bestGuess;
            }
            bestGuess = information.GetTile(information.description.width - 1, 0);
            if (bestGuess.IsHidden() && !witnessedSet.Contains(bestGuess) && !bestGuess.IsExcluded()) {
                return bestGuess;
            }
            bestGuess = information.GetTile(information.description.width - 1, information.description.height - 1);
            if (bestGuess.IsHidden() && !witnessedSet.Contains(bestGuess) && !bestGuess.IsExcluded()) {
                return bestGuess;
            }

            bestGuess = null;
            int bestGuessCount = 9;

            for (int x = 0; x < information.description.width; x++) {
                for (int y = 0; y < information.description.height; y++) {

                    SolverTile tile = information.GetTile(x, y);

                    if (tile.IsHidden() && !tile.IsExcluded() && !witnessedSet.Contains(tile)) {
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

        public static BigInteger Calculate(int mines, int squares) {
            return binomial.Generate(mines, squares);
        }

        public static void Initialise() {

        }
    }
}
