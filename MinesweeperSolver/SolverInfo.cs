using MinesweeperControl;
using System;
using System.Collections.Generic;
using static MinesweeperControl.MinesweeperGame;

namespace MinesweeperSolver {

    public class SolverInfo {

        private static readonly bool EXCLUDE_ON = false;

        // contains information about what surrounds a tile
        public class AdjacentInfo {

            public readonly int mines;
            public readonly int hidden;
            public readonly int excluded;

            public AdjacentInfo(int mines, int hidden, int excluded) {
                this.mines = mines;
                this.hidden = hidden;
                this.excluded = excluded;
            }

        }

        private readonly int key = new Random().Next();

        public readonly GameDescription description;
        public readonly bool verbose;

        private GameStatus gameStatus = GameStatus.NotStarted;
        private readonly SolverTile[,] tiles;
        private int tilesLeft;

        private ProbabilityEngine probabilityEngine = null;  // this is the probability engine from the last solver run
        private BruteForceAnalysis bfa = null;  // this is the brute force analysis from the last solver run

        private readonly HashSet<SolverTile> livingWitnesses = new HashSet<SolverTile>(20);   // this is a set of tiles which are clear and have hidden neighbours.
        private readonly HashSet<SolverTile> knownMines;   // this is a set of tiles which are known to be mines.
        private readonly HashSet<SolverTile> pendingClears = new HashSet<SolverTile>();   // this is a set of tiles which are clear and haven't yet been clicked.


        private readonly HashSet<SolverTile> deadTiles = new HashSet<SolverTile>();   // this is a set of tiles which are known to be dead.
        //private readonly HashSet<SolverTile> excludedWitnesses = new HashSet<SolverTile>();   // this is a set of witnesses which no longer influence the solving of the game.
        //private readonly HashSet<SolverTile> excludedTiles = new HashSet<SolverTile>();   // this is a set of tiles which no longer influence the solving of the game.
        //private int excludedMinesCount = 0; // number of mines which are with the exluded tiles area
 

        private readonly List<SolverTile> newClears = new List<SolverTile>();

        public SolverInfo(GameDescription description) : this(description, false) {
        }

        public SolverInfo(GameDescription description, bool verbose) {

            this.description = description;
            this.verbose = verbose;

            tiles = new SolverTile[description.width, description.height];

            // populate the grid with solver tiles showing what we know so far ... which is nothing
            for (int x = 0; x < description.width; x++) {
                for (int y = 0; y < description.height; y++) {
                    tiles[x, y] = new SolverTile(key, x, y);
                }
            }

            tilesLeft = description.height * description.width;

            knownMines = new HashSet<SolverTile>(description.mines);

        }

        public void AddInformation(GameResult information) {

            //long start = DateTime.Now.Ticks;

            gameStatus = information.status;

            this.probabilityEngine = null; // previous probability engine invalidated

            newClears.Clear();

            // the game results tell us when a tile is cleared, flagged or unflagged
            foreach (ActionResult ar in information.actionResults) {

                if (ar.resultType == ResultType.Cleared) {
                    tilesLeft--;
                    tiles[ar.x, ar.y].SetValue(ar.value);

                    SolverTile tile = tiles[ar.x, ar.y];
                    newClears.Add(tile);
                    if (tile.IsDead()) {
                        deadTiles.Remove(tile);
                    }
                    //if (tile.IsExcluded()) {
                    //    excludedTiles.Remove(tile);
                    //}
                    //pendingClears.Remove(tile);

                } else if (ar.resultType == ResultType.Flagged) {
                    tiles[ar.x, ar.y].SetFlagged(true);
                } else if (ar.resultType == ResultType.Hidden) {
                    tiles[ar.x, ar.y].SetFlagged(false);
                } else if (ar.resultType == ResultType.Exploded) {
                    SolverTile tile = tiles[ar.x, ar.y];
                    tile.SetFlagged(false);
                    MineFound(tile);
                    this.bfa = null;   // can't walk the bfa tree if we've trodden on a mine
                }

            }

            // find and mark tiles as exhausted
            //int removed = 0;
            //if (newClears.Count > 0) {

                 foreach (SolverTile tile in livingWitnesses) {

                    if (AdjacentTileInfo(tile).hidden == 0) {
                        tile.SetExhausted();
                     }
                }
                // remove all exhausted tiles from the list of witnesses
                livingWitnesses.RemoveWhere(x => x.IsExhausted());

            //}

            // add new witnesses which aren't exhausted
            foreach (SolverTile newTile in newClears) {
                AdjacentInfo adjInfo = AdjacentTileInfo(newTile);
                if (adjInfo.hidden != 0) {
                    if (adjInfo.excluded != adjInfo.hidden) {
                        livingWitnesses.Add(newTile);
                    } else {
                        //excludedWitnesses.Add(newTile);
                    }
                    
                }
            }

            //Write("There are " + livingWitnesses.Count + " living witnesses (" + removed + " deleted)");
            //Write("Adding Information to Solver took " + (DateTime.Now.Ticks - start) + " ticks");
        }

        public int GetMinesLeft() {
            return description.mines - knownMines.Count;    // mines left to find =  mines in game - mines found
        }

        public int GetTilesLeft() {
            return this.tilesLeft;
        }

        // sets the tile as a mine, stores it for future use and returns true if it is currently flagged
        public bool MineFound(SolverTile tile) {

            // if this is already known to be mine then nothing to do
            if (tile.IsMine()) {
                return tile.IsFlagged();
            }

            tilesLeft--;

            tile.SetAsMine(key);

            knownMines.Add(tile);

            if (tile.IsDead()) {
                deadTiles.Remove(tile);  // remove the tile if it was on the dead list
            }
            //if (tile.IsExcluded()) {
            //    excludedTiles.Remove(tile); // remove the tile if it was excluded
            //   excludedMinesCount--;
            //}

            return tile.IsFlagged();

        }

        public void SetTileToDead(SolverTile tile) {

            if (tile.IsMine()) {
                Write("ERROR: Trying to set a mine tile to dead " + tile.AsText());
                return;
            }

            tile.SetDead(key);    // mark it
            deadTiles.Add(tile);  //  and add to the set

        }

        public HashSet<SolverTile> GetDeadTiles() {
            return deadTiles;
        }

        /*
        public void ExcludeMines(int n) {

            if (!EXCLUDE_ON) {
                return;
            }

            this.excludedMinesCount = this.excludedMinesCount + n;
        }

        public int GetExcludedMineCount() {
            return this.excludedMinesCount;
        }

        public void ExcludeTile(SolverTile tile) {

            if (!EXCLUDE_ON) {
                return;
            }

            tile.SetExcluded();
            excludedTiles.Add(tile);
        }

        public HashSet<SolverTile> GetExcludedTiles() {
            return this.excludedTiles;
        }
 
        /// <summary>
        /// Move a tile from the list of witnesses to the list of excluded witnesses
        /// </summary>
        public void ExcludeWitness(SolverTile tile) {

            if (!EXCLUDE_ON) {
                return;
            }

            tile.SetExcluded();
            if (livingWitnesses.Remove(tile)) {
                excludedWitnesses.Add(tile);
            }
        }

        public HashSet<SolverTile> GetExcludedWitnesses() {
            return this.excludedWitnesses;
        }
        */

        public void SetBruteForceAnalysis(BruteForceAnalysis bfa) {
            this.bfa = bfa;
        }

        public BruteForceAnalysis GetBruteForceAnalysis() {
            return this.bfa;
        }

        public void SetProbabilityEngine(ProbabilityEngine pe) {
            this.probabilityEngine = pe;
        }

        // returns the probability the tile is safe if known. Or -1 otherwise.
        public double GetProbability(int x, int y) {

            // if we are out of bounds then nothing to say
            if (x < 0 || x >= description.width || y < 0 || y >= description.height) {
                return -1;
            }

            SolverTile tile = tiles[x, y];

            // an unflagged mine
            if (tile.IsMine() && !tile.IsFlagged()) {
                return 0;
            }

            // otherwise if revealed then nothing to say
            if (!tile.IsHidden()) {
                return -1;
            }

            //if (tile.IsExcluded()) {
            //    return -1;
            //}

            if (probabilityEngine != null) {
                return probabilityEngine.GetProbability(tile);
            } else if (pendingClears.Contains(tile)) {
                return 1;
            } else {
                return -1;
            }

        }

        //public bool IsTileDead(SolverTile tile) {
        //    return deadTiles.Contains(tile);
        //}

        // allow the gui to see if a tile is dead
        //public bool IsTileDead(int x, int y) {
        //    return IsTileDead(tiles[x, y]);
        //}

        // Adds the tile to a list of known clears for future use
        public void ClearFound(SolverTile tile) {
            pendingClears.Add(tile);
        }

        // returns all the indices adjacent to this index
        public List<SolverTile> GetAdjacentTiles(SolverTile tile) {

            // have we already calculated the adjacent tiles
            List<SolverTile> adjTiles = tile.GetAdjacentTiles();
            if (adjTiles != null) {
                return adjTiles;
            }

            adjTiles = new List<SolverTile>();

            int first_row = Math.Max(0, tile.y - 1);
            int last_row = Math.Min(description.height - 1, tile.y + 1);

            int first_col = Math.Max(0, tile.x - 1);
            int last_col = Math.Min(description.width - 1, tile.x + 1);

            for (int r = first_row; r <= last_row; r++) {
                for (int c = first_col; c <= last_col; c++) {
                    if (r != tile.y || c != tile.x) {
                        adjTiles.Add(tiles[c, r]);
                    }
                }
            }

            // remember them for next time
            tile.SetAdjacentTiles(adjTiles);

            return adjTiles;
        }

        public AdjacentInfo AdjacentTileInfo(SolverTile tile) {

            int hidden = 0;
            int mines = 0;
            int excluded = 0;
            foreach (SolverTile adjTile in GetAdjacentTiles(tile)) {
                if (adjTile.IsMine()) {
                    mines++;
                } else if (adjTile.IsHidden() ) {
                    hidden++;
                    //if (adjTile.IsExcluded()) {
                    //    excluded++;
                    //}
                }
            }

            return new AdjacentInfo(mines, hidden, excluded);
        }

        public GameStatus GetGameStatus() {
            return gameStatus;
        }

        public SolverTile GetTile(int x, int y) {
            return tiles[x, y];
        }

        public HashSet<SolverTile> GetWitnesses() {
            return livingWitnesses;
        }

        public HashSet<SolverTile> GetKnownMines() {
            return knownMines;
        }

        public void Write(string text) {
            if (verbose) {
                Console.WriteLine(text);
            }
        }
    }
}
