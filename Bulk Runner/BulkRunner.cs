using MinesweeperControl;
using MinesweeperSolver;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using static MinesweeperControl.GameDescription;
using static MinesweeperControl.MinesweeperGame;

namespace Bulk_Runner {
    class BulkRunner {

        private static readonly bool pauseOnWin = false;
        private static GameAction[] firstPlay;

        static void Main(string[] args) {

            GameDescription description = GameDescription.EXPERT_SAFE;
            //GameDescription description = new GameDescription(1000, 1000, 150000, GameType.Zero);
            //GameDescription description = new GameDescription(100, 100, 1500, GameType.Zero);
            //GameDescription description = new GameDescription(50, 50, 500, GameType.Safe);
            //GameDescription description = new GameDescription(8, 8, 34, GameType.Safe);

            if (description.gameType == GameType.Zero) {
                firstPlay = new GameAction[] { new GameAction(3, 3, ActionType.Clear) };
            } else {
                firstPlay = new GameAction[] { new GameAction(0, 0, ActionType.Clear) };
            }

            //int seedGen = new Random().Next();
            int seedGen = 19680503;

            Random rng = new Random(seedGen);

            int run = 50000;
            int steps = 10;

            int won = 0;
            int lost = 0;

            SolverMain.Initialise();

            Write("using generation seed " + seedGen + " to run " + run + " games of minesweeper " + description.AsText());
            Write("--- Press Enter to start ---");

            Console.ReadLine();

            long start = DateTime.Now.Ticks;

            for (int i=0; i < run; i++) {

                int seed = rng.Next();

                MinesweeperGame game = new MinesweeperGame(description, seed);

                Write("Seed " + game.seed + " starting");

                GameStatus status = AutoPlayRunner(game);

                if (status == GameStatus.Lost) {
                    lost++;
                } else if (status == GameStatus.Won) {
                    won++;
                    if (pauseOnWin) {
                        Write("Seed " + game.seed + " won");
                        Write("--- Press Enter to continue ---");
                        Console.ReadLine();
                    }
                } else {
                    Write("Seed " + game.seed + " : Unexpected game status from autoplay " + status);
                }

                if ((i + 1) % steps == 0) {
                    Write("Seed " + game.seed + " finished. Games won " + won + " out of " + (i + 1));
                }

            }

            long duration = (DateTime.Now.Ticks - start ) / 10000;


            Write("Games won " + won + ", lost " + lost + " out of " + run + " in " + duration + " milliseconds.");

            double winRate = (won * 10000 / run) / 100d;

            Write("Win rate " + winRate + "%");

            Console.ReadLine();


        }

        // this method runs on a different thread. gameNumber can be changed by clicking chosing a new game from the UI thread.
        private static GameStatus AutoPlayRunner(MinesweeperGame game) {

            //long start = DateTime.Now.Ticks;

            SolverActionHeader solverActions;

            GameResult result = game.ProcessActions(firstPlay);

            SolverInfo solverInfo = new SolverInfo(game.description);

            while (result.status == GameStatus.InPlay) {

                solverInfo.AddInformation(result);  // let the solver know what has happened

                solverActions = SolverMain.FindActions(solverInfo);   // pass the solver info into the solver and see what it comes up with

                if (solverActions.solverActions.Count == 0) {
                    Write("No actions returned by the solver!!");
                    break;
                }

                result = game.ProcessActions(solverActions.solverActions);

                //Write("Game controller returned " + result.actionResults.Count + " results from this action");

                //foreach (SolverAction action in solverActions) {
                //    Write("Playing " + action.AsText() + " probability " + action.safeProbability);
                //}

            }

            //long end = DateTime.Now.Ticks;

            //Write("game took " + (end - start) + " ticks");


            return result.status;
        }

        private static void Write(String text) {
            Console.WriteLine(text);
        }
    }
}
