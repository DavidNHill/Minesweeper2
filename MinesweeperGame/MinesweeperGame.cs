using System;
using System.Collections.Generic;
using static MinesweeperControl.GameDescription;

namespace MinesweeperControl {
    /*
    *  This class describes a game of minesweeper 
    */
    public class MinesweeperGame {

        public enum ActionType { Clear, Flag, Chord, Dead };
        public enum GameStatus { NotStarted, InPlay, Won, Lost };
        public enum ResultType { Cleared, Flagged, Hidden, Exploded, Mine, FlaggedWrong };

        // describes the actions being perform in the gane
        public class GameAction {

            public readonly int x;
            public readonly int y;
            public readonly ActionType action;

            public GameAction(int x, int y, ActionType action) {
                this.x = x;
                this.y = y;
                this.action = action;
            }

            public String AsText() {
                return "(" + x + "," + y + ") " + action;
            }
        }

        // describes the result of the actions on the game
        public class GameResult {

            public readonly GameStatus status;
            public readonly List<ActionResult> actionResults;

            public GameResult(GameStatus status, List<ActionResult> actionResults) {
                this.status = status;
                this.actionResults = actionResults;
            }

        }

        // describes the result of actions being perform in the gane
        public class ActionResult {

            public readonly int x;
            public readonly int y;
            public readonly ResultType resultType;
            public readonly int value;

            internal ActionResult(int x, int y, ResultType resultType) : this(x, y, resultType, 0) {
            }

            internal ActionResult(int x, int y, ResultType resultType, int value) {
                this.x = x;
                this.y = y;
                this.resultType = resultType;
                this.value = value;
            }
        }


        // game variables
        public readonly GameDescription description;
        public readonly int seed;    // seed to use to build the game

        private readonly int[] adjacentOffset = new int[8];

        private GameStatus gameStatus = GameStatus.NotStarted;
        private int tilesLeft;
        private MinesweeperTile[] tiles;
        private int minesLeft;

        private readonly bool hardcore;
        private int deaths = 0;

        public MinesweeperGame(GameDescription description, int seed) : this(description, seed, true) {
        }

        public MinesweeperGame(GameDescription description, int seed, bool hardcore) {

            if (seed == 0) {
                this.seed = new Random().Next(1, Int32.MaxValue);
            } else {
                this.seed = seed;
            }

            this.description = description;
            this.hardcore = hardcore;
            
            // create adjacent offsets
            adjacentOffset[0] = -description.width - 1;
            adjacentOffset[1] = -description.width;
            adjacentOffset[2] = -description.width + 1;
            adjacentOffset[3] = -1;
            adjacentOffset[4] = 1;
            adjacentOffset[5] = +description.width - 1;
            adjacentOffset[6] = +description.width;
            adjacentOffset[7] = +description.width + 1;

            tilesLeft = this.description.height * this.description.width - this.description.mines;
            minesLeft = description.mines;

            // create all the tiles. the mines get placed at the first clear.
            CreateTiles();

        }

        public int GetMinesLeft() {
            return minesLeft;
        }

        public int GetDeaths() {
            return deaths;
        }

        public GameStatus GetGameStatus() {
            return this.gameStatus;
        }

        public GameResult ProcessActions<T>(IList<T> actions) where T : GameAction {   // accept any Array of classes extending GameAction

            //long start = DateTime.Now.Ticks;

            List<ActionResult> actionResults = new List<ActionResult>();

            foreach (GameAction action in actions) {

                if (gameStatus != GameStatus.Lost && gameStatus != GameStatus.Won) {  // only process actions while the game is in play
                    if (action.action == ActionType.Clear) {
                        actionResults.AddRange(ClearTile(action));
                    } else if (action.action == ActionType.Flag) {
                        actionResults.AddRange(FlagTile(action));
                    } else if (action.action == ActionType.Chord) {
                        actionResults.AddRange(ChordTile(action));
                    }
                }

            }

            GameStatus finalStatus = gameStatus;  // take the game status for the game 

            if (gameStatus == GameStatus.Lost) {

                for (var i = 0; i < this.tiles.Length; i++) {
                    MinesweeperTile tile = this.tiles[i];
                    if (tile.IsMine() && !tile.IsFlagged() && !tile.GetExploded()) {
                        actionResults.Add(new ActionResult(tile.GetX(), tile.GetY(), ResultType.Mine));  // show remaining mines
                    }
                    if (!tile.IsMine() && tile.IsFlagged()) {
                        actionResults.Add(new ActionResult(tile.GetX(), tile.GetY(), ResultType.FlaggedWrong));  // show flags placed wrong 
                    }
                }

            }

            //Write("Game processing actions took " + (DateTime.Now.Ticks - start) + " ticks");

            return new GameResult(finalStatus, actionResults);

        }

        private List<ActionResult> FlagTile(GameAction action) {

            List<ActionResult> actionResults = new List<ActionResult>();

            int index = GetIndex(action.x, action.y);

            MinesweeperTile tile = this.tiles[index];

            // if the tile is covered then toggle the flag and return it's new state
            if (tile.IsCovered()) {
                tile.ToggleFlagged();
                if (tile.IsFlagged()) {
                    actionResults.Add(new ActionResult(action.x, action.y, ResultType.Flagged));
                    minesLeft--;
                } else {
                    if (tile.GetExploded()) {
                        actionResults.Add(new ActionResult(action.x, action.y, ResultType.Exploded));
                    } else {
                        actionResults.Add(new ActionResult(action.x, action.y, ResultType.Hidden));
                    }
 
                    minesLeft++;
                }
            }

            return actionResults;

        }


        // clicks the assigned tile and returns an object containing a list of tiles cleared
        private List<ActionResult> ClearTile(GameAction action) {

            List<ActionResult> actionResults = new List<ActionResult>();

            int index = GetIndex(action.x, action.y);

            // if this is the first click then create the tiles and place the mines
            if (this.gameStatus == GameStatus.NotStarted) {
                PlaceMines(index);
            }

            MinesweeperTile tile = this.tiles[index];

            // are we clicking on a flag
            if (tile.IsFlagged()) {
                Write("Unable to Clear: Clicked on a Flag");

            } else if (tile.GetExploded()) {
                Write("Unable to Clear: Clicked on an exploded Mine");
 
            } else if (tile.IsMine()) {
                //Write("Gameover: Clicked on a mine");

                deaths++;
                if (hardcore) {
                    this.gameStatus = GameStatus.Lost;
                }
                tile.SetExploded(true);
                actionResults.Add(new ActionResult(action.x, action.y, ResultType.Exploded));

            } else {
                if (tile.IsCovered()) {    // make sure the tile is clickable

                    List<MinesweeperTile> tilesToReveal = new List<MinesweeperTile>();
                    tilesToReveal.Add(tile);

                    actionResults.AddRange(Reveal(tilesToReveal));

                }
            }

            return actionResults;

        }

        // chord the assigned tile and returns an object containing a list of tiles cleared
        private List<ActionResult> ChordTile(GameAction action) {

            List<ActionResult> actionResults = new List<ActionResult>();

            int index = GetIndex(action.x, action.y);

            // if this is the first click then create the tiles and place the mines
            if (this.gameStatus == GameStatus.NotStarted) {
                PlaceMines(index);
            }

            MinesweeperTile tile = this.tiles[index];

            int flagCount = 0;
            int hiddenCount = 0;
            foreach (int adjIndex in GetAdjacentIndex(index)) {
                if (tiles[adjIndex].IsFlagged()) {
                    flagCount++;
                } else if (tiles[adjIndex].IsCovered()) {
                    hiddenCount++;
                }
            }

            // If the hidden count is zero then there is nothing to do
            if (hiddenCount == 0) {
                Write("Unable to Chord: Nothing to clear");
                return actionResults;
            }

            // nothing to do if the tile is not yet surrounded by the correct number of flags
            if (tile.GetValue() != flagCount) {
                Write("Unable to Chord: value=" + tile.GetValue() + " flags=" + flagCount);
                return actionResults;
            }

            // see if there are any unflagged bombs in the area to be chorded - this loses the game
            var bombCount = 0;
            foreach (int adjIndex in GetAdjacentIndex(index)) {
                MinesweeperTile adjTile = tiles[adjIndex];
                if (adjTile.IsMine() && !adjTile.IsFlagged()) {
                    adjTile.SetExploded(true);
                    actionResults.Add(new ActionResult(adjTile.GetX(), adjTile.GetY(), ResultType.Exploded));  // mark the tile as exploded
                    bombCount++;
                }
            }

            // if we have triggered a bomb then return
            if (bombCount != 0) {
                deaths = deaths + bombCount;
                if (hardcore) {
                    this.gameStatus = GameStatus.Lost;
                }
                return actionResults;
            }

            // seems okay, so do the chording
            List<MinesweeperTile> tilesToReveal = new List<MinesweeperTile>();

            // determine which tiles need revealing 
            foreach (int adjIndex in this.GetAdjacentIndex(index)) {
                MinesweeperTile adjTile = tiles[adjIndex];
                if (adjTile.IsCovered() && !adjTile.IsFlagged()) {  // covered and not flagged
                    tilesToReveal.Add(adjTile);
                }
            }

            actionResults.AddRange(Reveal(tilesToReveal));

            return actionResults;

        }

        // takes a list of tiles and reveals them and expands any zeros
        private List<ActionResult> Reveal(List<MinesweeperTile> firstTiles) {

            List<ActionResult> actionResults = new List<ActionResult>();

            var soFar = 0;

            foreach (MinesweeperTile firstTile in firstTiles) {
                firstTile.SetCovered(false);
            }

            int safety = 1000000;

            while (soFar < firstTiles.Count) {

                MinesweeperTile tile = firstTiles[soFar];

                actionResults.Add(new ActionResult(tile.GetX(), tile.GetY(), ResultType.Cleared, tile.GetValue()));

                this.tilesLeft--;

                // if the value is zero then for each adjacent tile not yet revealed add it to the list
                if (tile.GetValue() == 0) {

                    foreach (int adjIndex in GetAdjacentIndex(tile.GetIndex())) {

                        MinesweeperTile adjTile = this.tiles[adjIndex];

                        if (adjTile.IsCovered() && !adjTile.IsFlagged()) {  // if not covered and not a flag
                            adjTile.SetCovered(false);  // it will be uncovered in a bit
                            firstTiles.Add(adjTile);
                        }
                    }

                }

                soFar++;
                if (safety-- < 0) {
                    Write("MinesweeperGame: Reveal Safety limit reached !!");
                    break;
                }

            }

            // if there are no tiles left to find then set the remaining tiles to flagged and we've won
            if (this.tilesLeft == 0) {
                for (var i = 0; i < this.tiles.Length; i++) {
                    MinesweeperTile tile = this.tiles[i];
                    if (tile.IsMine() && !tile.IsFlagged()) {

                        minesLeft--;
                        tile.ToggleFlagged();
                        actionResults.Add(new ActionResult(tile.GetX(), tile.GetY(), ResultType.Flagged));  // auto set remaining flags

                    }
                }

                this.gameStatus = GameStatus.Won;

            }

            return actionResults;
        }

        // converts X and Y position to an Index
        private int GetIndex(int x, int y) {
            return y * this.description.width + x;
        }

        // create the tiles 
        private void CreateTiles() {

            //long start = DateTime.Now.Ticks;

            tiles = new MinesweeperTile[this.description.width * this.description.height];

            // create the tiles and store non-excluded indices into a list
            for (int y = 0; y < this.description.height; y++) {
                for (int x = 0; x < this.description.width; x++) {

                    int i = GetIndex(x, y);
                    tiles[i] = new MinesweeperTile(i, x, y);

                }
            }

            //Write("Ticks to create MinesweeperTiles " + (DateTime.Now.Ticks - start));
        }

        // builds all the tiles and assigns bombs to them
        private void PlaceMines(int firstIndex) {

            //long start = DateTime.Now.Ticks;

            // hold the tiles to exclude from being a mine 
            HashSet<int> excludedIndices = new HashSet<int>();
            excludedIndices.Add(firstIndex);

            // for a zero start game all the adjacent tile can't be mines either
            if (this.description.gameType == GameType.Zero) {
                foreach (int adjIndex in GetAdjacentIndex(firstIndex)) {
                    excludedIndices.Add(adjIndex);
                }
            }

            // create a list of all included indices
            List<int> indices = new List<int>();
            for (int y = 0; y < this.description.height; y++) {
                for (int x = 0; x < this.description.width; x++) {

                    int i = GetIndex(x, y);
                    if (!excludedIndices.Contains(i)) {
                        indices.Add(i);
                    }
                }
            }

            // shuffle the indices using a seed
            indices.Shuffle(seed);

            // allocate the mines and calculate the values
            for (int i = 0; i < description.mines; i++) {
                int index = indices[i];
                MinesweeperTile tile = tiles[index];

                //Utility.Write("Setting " + tile.AsText() + " to be a mine");
                tile.SetMine(true);   // this is set to be a mine

                // set each affected tile to have an increased 'value' 
                foreach (int adjIndex in GetAdjacentIndex(tile.GetIndex())) {
                    tiles[adjIndex].IncrementValue();
                }
            }

            this.gameStatus = GameStatus.InPlay;

            //Write("Ticks to place mines on MinesweeperTiles " + (DateTime.Now.Ticks - start));
        }

        // returns all the indices adjacent to this index
        private List<int> GetAdjacentIndex(int index) {

            int col = index % description.width;
            int row = index / description.width;

            int first_row = Math.Max(0, row - 1);
            int last_row = Math.Min(description.height - 1, row + 1);

            int first_col = Math.Max(0, col - 1);
            int last_col = Math.Min(description.width - 1, col + 1);

            List<int> result = new List<int>();

            for (int r = first_row; r <= last_row; r++) {
                for (int c = first_col; c <= last_col; c++) {
                    int i = description.width * r + c;
                    if (i != index) {
                        result.Add(i);
                    }
                }
            }

            return result;
        }

        private void Write(String text) {
            Console.WriteLine(text);
        }

    }

    public static class Shuffler {

        // shuffle a given array
        public static void Shuffle<T>(this IList<T> list, int seed) {

            Random rng = new Random(seed);

            int n = list.Count;
            while (n > 1) {
                n--;
                int k = rng.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }

        }

    }

}
