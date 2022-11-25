
using PS4RemotePlayInterceptor;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace PenetratorBot
{
    internal class Penetrator
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetDC(IntPtr window);
        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern uint GetPixel(IntPtr dc, int x, int y);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern int ReleaseDC(IntPtr window, IntPtr dc);

        static Rectangle resolution = System.Windows.Forms.Screen.PrimaryScreen.Bounds;

        // constant for already processed position
        const int PROCESSED_POSITION = 20;
        const int NONE = -1;
        const int maxStoredMoves = 20;

        // button press length is quick to ensure fast enough movement
        const int DEFAULT_PRESS_TICKS = 12;
        // weapon doesn't fire sometimes with quick button presses so we need to ensure this button press islonger so it
        // has a better chance of registering with the console
        const int FIRE_PRESS_TICKS = 20;
        const int NUM_RELEASE_TICKS = 12;

        static readonly (int, int) NATIVE_RESOLUTION = (2560, 1440);

        private readonly object DualShockStateLock = new object();

        static readonly double xMultiple = (double)resolution.Width / NATIVE_RESOLUTION.Item1;
        static readonly double yMultiple = (double)resolution.Height / NATIVE_RESOLUTION.Item2;

        static readonly (int, int)[] enemyPositions =
                  {(1210, 660), (1256, 660), (1304, 660), (1350, 660),
                   (1397, 708), (1397, 754), (1397, 802), (1397, 848),
                   (1350, 895), (1304, 895), (1256, 895), (1210, 895),
                   (1162, 848), (1162, 802), (1162, 754), (1162, 708) };
        static readonly int numEnemyPositions = enemyPositions.Length;

        static readonly (int, int)[] firePositions = 
                         {(1015, 315), (1190, 315), (1365, 315), (1540, 315),
                          (1740, 510), (1740, 685), (1740, 860), (1740, 1035),
                          (1540, 1235), (1365, 1235), (1190, 1235), (1015, 1235),
                          (820, 1035), (820, 860), (820, 685), (820, 510)};

        static readonly (int, int) penetratedTuple = ((int)Math.Round(1165 * xMultiple), 
                                                       (int)Math.Round(772 * yMultiple));

        // array of enemy locations
        long[] Enemies { get; set; } = new long[numEnemyPositions];
        int[] NextFirePosition { get; set; } = new int[maxStoredMoves];
        int NextIndexAvailable { get; set; } = 0;
        int numLives = 3;
        public bool EndProgram { get; set; }
        public long TickCounter { get; set; }
        public DualShockState CurrentControllerState { get; private set; }
        public bool Died { get; set; }

        public Penetrator() { }

        internal void PlayGame()
        {
            // start the game
            PressButtons(new DualShockState() { Cross = true });
            Thread.Sleep(2000);
            PressButtons(new DualShockState() { Cross = true });
            Thread.Sleep(2000);

            while (numLives > 0 && !EndProgram)
            {
                StartNewLife();

                if (!EndProgram) {
                    // decrement our life counter
                    numLives--;
                    // sleep 3 seconds to let the death animation finish
                    Thread.Sleep(3000);
                }
            }
        }

        private void StartNewLife()
        {
            Console.WriteLine("Staring new life. " + numLives + " lives left.");
            Died = false;
            // reset all the positioning data
            for (int i=0; i<numEnemyPositions; i++)
            {
                Enemies[i] = NONE;
            }
            for (int i=0; i< maxStoredMoves; i++)
            {
                NextFirePosition[i] = 0;
            }
            NextIndexAvailable = 0;

            // start thread to search for enemies
            Thread findEnemiesThread = new Thread(FindEnemies);
            findEnemiesThread.Start();

            // now move and fire based on enemy search thread
            MoveAndFire();

            // if we got here then we died or were asked to end, join the thread and return
            findEnemiesThread.Join();
        }

        private void MoveAndFire()
        {
            int currentPosition = 1;
            int positionOffset = 0;
            int currentIndex = 0;

            while (!EndProgram && !Died)
            {
                // check if we have anywhere to move and fire
                if (NextFirePosition[currentIndex] != 0)
                {
                    // check if position was processed already
                    if (NextFirePosition[currentIndex] != PROCESSED_POSITION)
                    {
                        positionOffset = CalculatePositionOffset(currentPosition, NextFirePosition[currentIndex]);

                        if (positionOffset != 0)
                        {
                            // now move in the quickest direction
                            MoveWeapon(NextFirePosition[currentIndex], positionOffset);
                        } else {
                            // fire the weapon twice in case additional enemies appear here and
                            // one of the enemies moves faster than expected, thus eluding our detection
                            // algorithm
                            FireWeapon();
                        }

                        // we are at the correct position, fire our weapon
                        FireWeapon();

                        // update position
                        currentPosition = NextFirePosition[currentIndex];

                        // now check if there are anymore enemies at this same spot before we move
                        for (int i = 0; i < maxStoredMoves; i++)
                        {
                            if (i != currentIndex && NextFirePosition[i] == currentPosition)
                            {
                                // fire the weapon again since we are already at this position
                                // fire the weapon twice in case additional enemies appear here and
                                // one of the enemies moves faster than expected, thus eluding our detection
                                // algorithm
                                FireWeapon();
                                FireWeapon();
                                NextFirePosition[i] = PROCESSED_POSITION;
                            }
                        }
                    }

                    // reset position
                    NextFirePosition[currentIndex] = 0;

                    // set array index to next movement position
                    currentIndex += 1;
                    currentIndex %= maxStoredMoves;
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
        }

        private int CalculatePositionOffset(int startPosition, int endPosition)
        {
            int positionOffset = startPosition - endPosition;

            // check if we need to reverse our direction
            int absPositionOffset = Math.Abs(positionOffset);
            if (absPositionOffset > (numEnemyPositions / 2))
            {
                if (positionOffset > 0)
                    positionOffset = -1 * (numEnemyPositions - absPositionOffset);
                else
                    positionOffset = numEnemyPositions - absPositionOffset;
            }

            return positionOffset;
        }

        private void FireWeapon()
        {
            Console.WriteLine("Firing Weapon");
            PressButtons(new DualShockState() { Cross = true }, FIRE_PRESS_TICKS);
        }

        private void MoveWeapon(int expectedPosition, int moveCount)
        {
            if (!EndProgram && !Died)
            {
                Console.WriteLine("Moving " + (-1 * moveCount) + " to position " + expectedPosition);
                int absMoveCount = Math.Abs(moveCount);
                for (int i = 0; (i < absMoveCount) && !EndProgram && !Died; i++)
                {
                    if (moveCount > 0)
                    {
                        // move counterclockwise until we are there
                        PressButtons(new DualShockState() { DPad_Left = true });
                    }
                    else
                    {
                        // move clockwise until we are there
                        PressButtons(new DualShockState() { DPad_Right = true });
                    }
                }

                // the moves don't always register correctly via remote play so let's make sure we are in the right position
                Thread.Sleep(200);  // wait for screen to update the last move
                if (GetColorAt((int)Math.Round(firePositions[expectedPosition - 1].Item1 * xMultiple),
                               (int)Math.Round(firePositions[expectedPosition - 1].Item2 * yMultiple)).R < 100)
                {
                    // we are not where we are supposed to be, find our current position
                    for (int i = 0; i < numEnemyPositions; i++)
                    {
                        if (GetColorAt((int)Math.Round(firePositions[i].Item1 * xMultiple),
                                       (int)Math.Round(firePositions[i].Item2 * yMultiple)).R > 100)
                        {
                            Console.WriteLine("Position " + i + " incorrect, adjusting...");
                            // adjust our position
                            MoveWeapon(expectedPosition, CalculatePositionOffset(i + 1, expectedPosition));

                            break;
                        }
                    }
                }
            }
        }


        private Color GetColorAt(int x, int y)
        {
            IntPtr dc = GetDC(IntPtr.Zero);
            int rgb = (int)GetPixel(dc, x, y);
            ReleaseDC(IntPtr.Zero, dc);

            return Color.FromArgb((rgb >> 0) & 0xff, (rgb >> 8) & 0xff, (rgb >> 16) & 0xff);
        }

        private void FindEnemies()
        {
            long timeDiff = 0;
            long currentTime;
            Color pixelColor;

            // loop until we run out of lives or quit the program
            while (!EndProgram)
            {
                // first check if we died
                pixelColor = GetColorAt((int)Math.Round(penetratedTuple.Item1 * xMultiple),
                                        (int)Math.Round(penetratedTuple.Item2 * yMultiple));
                if ((pixelColor.R > 100) && (pixelColor.G > 100)) {
                    Console.WriteLine("We died :(");
                    Died = true;

                    // end the thread so we can start our next life
                    break;
                }

                currentTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                for (int index=0; index<numEnemyPositions; index++)
                {
                    pixelColor = GetColorAt((int)Math.Round(enemyPositions[index].Item1 * xMultiple), 
                                            (int)Math.Round(enemyPositions[index].Item2 * yMultiple));

                    if (pixelColor.G > 100) {
                        if (Enemies[index] != NONE) {
                            timeDiff = currentTime - Enemies[index];
                        }
                        // enemies takes typically between 1000 - 1200 millseconds to move after they spawn
                        // however, a third enemy at the same position can move after ~700ms!
                        if (Enemies[index] == NONE || timeDiff > 1200) {
                            Enemies[index] = currentTime;
                            Console.WriteLine("Found enemy at position " + (index + 1));

                            // set the fire position in our array
                            NextFirePosition[NextIndexAvailable] = index + 1;
                            NextIndexAvailable++;
                            NextIndexAvailable %= maxStoredMoves;
                        }
                    } else if (pixelColor.G < 100 && Enemies[index] != NONE) {
                        Enemies[index] = NONE;
                    }
                }

                Thread.Sleep(20);
            }
        }

        private void PressButtons(DualShockState state, int tickLength = DEFAULT_PRESS_TICKS)
        {
            // reset input tick counter
            TickCounter = 0;

            // need to be concurrent with access to this vairable in OnReceiveData method
            lock (DualShockStateLock)
            {
                // set the state of the DualShock controller for n ticks
                CurrentControllerState = state;
            }
            while (TickCounter < tickLength)
            {
                Thread.Sleep(2);
            }

            // reset input tick counter
            TickCounter = 0;

            // need to be concurrent with access to this vairable in OnReceiveData method
            lock (DualShockStateLock)
            {
                // clear the state of the DualShock controller for x ticks
                CurrentControllerState = null;
            }
            while (TickCounter < NUM_RELEASE_TICKS)
            {
                Thread.Sleep(2);
            }
        }

        internal void OnReceiveData(ref DualShockState state)
        {
            // need to be concurrent with setting CurrentControllerState in PressButtons method
            lock (DualShockStateLock)
            {
                if (CurrentControllerState != null)
                {
                    state = CurrentControllerState;
                    state.ReportTimeStamp = DateTime.Now;

                    // Replace battery status
                    state.Battery = 100;
                    state.IsCharging = true;
                }
            }

            // now update tick counter
            TickCounter++;
        }
    }
}