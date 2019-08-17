using System;
using System.Collections.Generic;
using System.Text;

namespace MinesweeperControl {

    public class GameDescription {

        public enum GameType { Safe, Zero };

        public static readonly GameDescription BEGINNER_SAFE =  new GameDescription(8, 8, 10, GameType.Safe);
        public static readonly GameDescription INTERMEDIATE_SAFE = new GameDescription(16, 16, 40, GameType.Safe);
        public static readonly GameDescription EXPERT_SAFE = new GameDescription(30, 16, 99, GameType.Safe);

        public static readonly GameDescription BEGINNER_ZERO = new GameDescription(9, 9, 10, GameType.Zero);
        public static readonly GameDescription INTERMEDIATE_ZERO = new GameDescription(16, 16, 40, GameType.Zero);
        public static readonly GameDescription EXPERT_ZERO = new GameDescription(30, 16, 99, GameType.Zero);

        public readonly int width;
        public readonly int height;
        public readonly int mines;
        public readonly GameType gameType;

        public GameDescription(int width, int height, int mines, GameType gameType) {

            this.width = width;
            this.height = height;
            this.mines = mines;
            this.gameType = gameType;

        }

        public string AsText() {

            return width + "x" + height + "x" + mines + " " + gameType + " start";
        }

    }
}
