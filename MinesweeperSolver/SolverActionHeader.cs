using System.Collections.Generic;

namespace MinesweeperSolver {

    class SolverActionHeader {

        public readonly IList<SolverAction> solverActions;
        public readonly IList<SolverAction> deadActions;

        public SolverActionHeader() {
            List<SolverAction> empty = new List<SolverAction>();
            this.solverActions = empty.AsReadOnly();
            this.deadActions = empty.AsReadOnly();
        }

        public SolverActionHeader(List<SolverAction> solverActions, List<SolverAction> deadActions) {
            this.solverActions = solverActions.AsReadOnly();
            this.deadActions = deadActions.AsReadOnly();
        }

    }
}
