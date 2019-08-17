using System;
using System.Collections.Generic;
using System.Text;

namespace MinesweeperControl {

    internal class MinesweeperTile {

        private readonly int index;
        private readonly int x;
        private readonly int y;
        private bool covered = true;
        private int value = 0;
        private bool flagged = false;
        private bool exploded = false;
        private bool mine;

        internal MinesweeperTile(int index, int x, int y) {

            this.index = index;
            this.x = x;
            this.y = y;

        }

        internal int GetIndex() {
            return index;
        }

        internal int GetX() {
            return x;
        }

        internal int GetY() {
            return y;
        }

        internal bool IsCovered() {
            return covered;
        }

        internal void SetCovered(bool value) {
            covered = value;
        }

        internal int GetValue() {
            return value;
        }

        internal void IncrementValue() {
            this.value++;
        }

        internal bool IsFlagged() {
            return flagged;
        }

        internal void ToggleFlagged() {
            flagged = !flagged;
        }

        internal bool GetExploded() {
            return exploded;
        }

        internal void SetExploded(bool value) {
            exploded = value;
        }

        internal bool IsMine() {
            return mine;
        }

        internal void SetMine(bool value) {
            mine = value;
        }

        internal String AsText() {
            return "(" + x + "," + y + ")";
        }
    }
}
