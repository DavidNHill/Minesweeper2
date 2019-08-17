using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace MinesweeperSolver {

    public class PrimeSieve {

        // iterator for prime numbers
        public class Primes : IEnumerable<int>, IEnumerator<int> {

            private int index = 0;
            private readonly int stop;
            private int nextPrime;
            private readonly bool[] composite;


            public int Current => Next();

            object IEnumerator.Current => Next();

            public Primes(bool[] composite, int start, int stop) {
                this.index = start;
                this.stop = stop;
                this.composite = composite;
                this.nextPrime = findNext();
            }

            public int Next() {
                int result = nextPrime;
                nextPrime = findNext();

                return result;
            }

            private int findNext() {

                int next = -1;
                while (index <= stop && next == -1) {
                    if (!composite[index]) {
                        next = index;
                    }
                    index++;
                }

                return next;

            }

            public void Dispose() {
                
            }

            public bool MoveNext() {
                return (nextPrime != -1);
            }

            public void Reset() {
                throw new NotImplementedException();
            }

            public IEnumerator<int> GetEnumerator() {
                return this;
            }

            IEnumerator IEnumerable.GetEnumerator() {
                return this;
            }
        }


        private readonly bool[] composite;
	    private readonly int max;

        public PrimeSieve(int n) {

            if (n < 2) {
                max = 2;
            } else {
                max = n;
            }

            composite = new bool[max + 1];

            int rootN = (int)Math.Floor(Math.Sqrt(n));

            for (int i = 2; i < rootN; i++) {

                // if this is a prime number (not composite) then sieve the array
                if (!composite[i]) {
                    int index = i + i;
                    while (index <= max) {
                        composite[index] = true;
                        index = index + i;
                    }
                }
            }

        }



        public bool IsPrime(int n) {
		    if (n <= 1 || n > max) {
                throw new Exception("Test value " + n + " is out of range 2 - " + max);
            }
		
		    return !composite [n];
        }

        public IEnumerable<int> getPrimesIterable(int start, int stop) {
    	
    	    if (start > stop) {
                throw new Exception("start " + start + " must be <= to stop " + stop);
            }
		    if (start <= 1 || start > max) {
                throw new Exception("Start value " + start + " is out of range 2 - " + max);
            }
		    if (stop <= 1 || stop > max) {
                throw new Exception("Stop value " + stop + " is out of range 2 - " + max);
            }
    	
    	    return new Primes(composite, start, stop);
    }
	
}

}
