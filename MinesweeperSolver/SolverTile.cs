using System;
using System.Collections.Generic;
using System.Text;
using static MinesweeperSolver.SolverInfo;

namespace MinesweeperSolver {

    public class SolverTile {

        public readonly int x;
        public readonly int y;
        private readonly long key;  // the key identifies which SolverInfo created these tiles and locks down some methods.
        // details about me
        private bool isMine = false;
        private bool isFlagged = false;
        private bool isHidden = true;
        private int value = 0;
        private bool isDead = false;     // a dead tile is one which provides no information if clicked. Should be avoided unless all tiles are dead.

        private bool exhausted = false;    // an exhausted tile is where it is cleared and all its neighbours are either cleared or a mine

        private bool newGrowth = true;     // a tile is new growth the first time it is revealed. Once it has been analysed by the Probability Engine it stops being new growth.

        private bool excluded = false;    // an excluded tile is one which no longer needs to be included in the propability engine since it no longer affects other tiles

        private List<SolverTile> adjacentTiles = null;

        public SolverTile(long key, int x, int y) {

            this.key = key;
            this.x = x;
            this.y = y;

        }

        public bool IsMine() {
            return isMine;
        }

        // this should only be called from the SolverInfo class
        public void SetAsMine(long key) {

            if (key != this.key) {
                throw new Exception("Unauthorised access to the SetAsMine method");
            }

            this.isMine = true;
            this.isHidden = false;  // no longer hidden we know it is a mine
        }

        public bool IsFlagged() {
            return this.isFlagged;
        }

        public void SetFlagged(bool flagged) {
            isFlagged = flagged;
        }

        public bool IsHidden() {
            return this.isHidden;
        }

        public void SetValue(int value) {
            this.isHidden = false;
            this.value = value;
        }

        public int GetValue() {
            return this.value;
        }

        public bool IsExhausted() {
            return this.exhausted;
        }

        public void SetExhausted() {
            this.exhausted = true;
        }

        // this should only be called from from the SolverInfo class
        public void SetDead(long key) {
            if (key != this.key) {
                throw new Exception("Unauthorised access to the SetDead method");
            }
            this.isDead = true;
        }

        public bool IsNewGrowth() {
            return newGrowth;
        }

        public void SetExamined() {
            this.newGrowth = false;
        }

        public bool IsDead() {
 
            return isDead;
        }

        public List<SolverTile> GetAdjacentTiles() {
            return this.adjacentTiles;
        }

        public void SetAdjacentTiles(List<SolverTile> adjTiles) {
            this.adjacentTiles = adjTiles;
        }

        public void SetExcluded() {
            this.excluded = true;
        }

        public bool IsExcluded() {
            return this.excluded;
        }

        public bool IsEqual(SolverTile tile) {
            if (this.x == tile.x && this.y == tile.y) {
                return true;
            } else {
                return false;
            }
        }

        // returns true if the tile provided is adjacent to this tile
        public bool IsAdjacent(SolverTile tile) {

            var dx = Math.Abs(this.x - tile.x);
            if (dx > 1) {
                return false;
            }

            var dy = Math.Abs(this.y - tile.y);
            if (dy > 1) {
                return false;
            }

            if (dx == 0 && dy == 0) {
                return false;
            } else {
                return true;
            }

            /*
            // adjacent and not equal
            if (dx < 2 && dy < 2 && !(dx == 0 && dy == 0)) {
                return true;
            } else {
                return false;
            }
            */
        }

        //<summary> return the number of hidden tiles shared by these tiles
        public int NumberOfSharedHiddenTiles(SolverTile tile) {

            // if the locations are too far apart they can't share any of the same squares
            if (Math.Abs(tile.x - this.x) > 2 || Math.Abs(tile.y - this.y) > 2) {
                return 0;
            }

            int count = 0;
            foreach (SolverTile tile1 in this.GetAdjacentTiles()) {
                if (!tile1.IsHidden()) {
                    continue;
                }
                foreach (SolverTile tile2 in tile.GetAdjacentTiles()) {
                    if (!tile2.IsHidden()) {
                        continue;
                    }
                    if (tile1.IsEqual(tile2)) {  // if they share a tile then return true
                        count ++;
                    }
                }
            }

            // no shared tile found
            return count;

        }


        public string AsText() {
            return "(" + x + "," + y + ")";
        }
    }
}
