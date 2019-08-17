using System;
using System.Numerics;

namespace MinesweeperSolver {
    public class Combination {

        public static BigInteger Calculate(int mines, int squares) {

            long start = DateTime.Now.Ticks;

            BigInteger top = 1;
            BigInteger bot = 1;

            var range = Math.Min(mines, squares - mines);

            // calculate the combination. 
            for (int i = 0; i < range; i++) {
                top = top * (squares - i);
                bot = bot * (i + 1);
            }

            BigInteger result = top / bot;

            //SolverMain.Write(squares + " pick " + mines + " in " + result + " ways");
            //SolverMain.Write("Combination duration " + (DateTime.Now.Ticks - start) + " ticks");

            return result;

        }
 
        private static readonly BigInteger[] power10n = { BigInteger.One, new BigInteger(10), new BigInteger(100), new BigInteger(1000), new BigInteger(10000), new BigInteger(100000), new BigInteger(1000000) };
        private static readonly int[] power10 = { 1, 10, 100, 1000, 10000, 100000, 1000000 };

        public static double DivideBigIntegerToDouble(BigInteger numerator, BigInteger denominator, int dp) {

            var work = numerator * power10n[dp] / denominator;

            var result = (double) work / power10[dp];

            return result;
        }


    }
}
