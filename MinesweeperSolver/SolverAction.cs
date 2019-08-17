using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using MinesweeperControl;
using static MinesweeperControl.MinesweeperGame;

namespace MinesweeperSolver {
    public class SolverAction : GameAction,  IComparable<SolverAction> {

        public readonly double safeProbability;

        public readonly bool isDead;
        public readonly bool isExcluded;

        public SolverAction(SolverTile tile, ActionType action, double safeprob) : base(tile.x, tile.y, action) {

            this.safeProbability = safeprob;
            this.isDead = tile.IsDead();
            this.isExcluded = tile.IsExcluded();
        }

        public int CompareTo(SolverAction other) {
            return safeProbability.CompareTo(other.safeProbability);
        }
    }
}
