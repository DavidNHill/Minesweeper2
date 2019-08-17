using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MinesweeperControl;
using MinesweeperSolver;
using static MinesweeperControl.GameDescription;
using static MinesweeperControl.MinesweeperGame;

namespace MinesweeperGui {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        private readonly bool verbose = false;  // determines whether diagnostic info gets written to the console

        private readonly ImageSource Hidden;
        private readonly ImageSource[] MineValue = new ImageSource[9];
        private readonly ImageSource Flagged;
        private readonly ImageSource FlaggedWrong;
        private readonly ImageSource Mine;
        private readonly ImageSource MineWrong;

        private readonly ImageSource[] LedValue = new ImageSource[10];

        private int tileSize = 32;

        private GameDescription gameDescription = GameDescription.EXPERT_SAFE;

        // details about the solver
        private SolverInfo solverInfo;
        private SolverActionHeader solverActions = new SolverActionHeader();

        // graphical components
        private Image hintImage;             // overlay of hints from the solver
        private Image totalImage;             // composite image of imageDrawings
        private DrawingGroup imageDrawings;  // contains all the tiles
        private ImageDrawing[,] images;  // all the tiles
        private readonly Image[] digits = new Image[8];

        // tool tip elements
        private Popup gameToolTip = new Popup();
        private readonly TextBox popupText = new TextBox();

        // details about the visible board
        private int topX = 0;
        private int topY = 0;
        private int boardWidth = 0;
        private int boardHeight = 0;
        private ActionResult[,] tiles;
        private MinesweeperGame game;
        private int minesLeftSize = 6;

        private bool useSolver = false;
        private bool autoRunning = false;
        private bool useGuesses = false;
        private bool logicalLock = false;
        private int gameNumber = 1;
        private int autoPlayMinDelay = 250;
        private SolverAction jumpTo;

        public object MinesweeperMain { get; private set; }

        public MainWindow() {
            Utility.Write("At MainWindow constructor");
            InitializeComponent();

            // load the images we need 
            Hidden = Utility.BuildImageSource("facingDown.png", tileSize);
            Flagged = Utility.BuildImageSource("flagged.png", tileSize);
            FlaggedWrong = Utility.BuildImageSource("flaggedWrong.png", tileSize);
            Mine = Utility.BuildImageSource("mine.png", tileSize);
            MineWrong = Utility.BuildImageSource("exploded.png", tileSize);

            for (int i = 0; i < 9; i++) {
                MineValue[i] = Utility.BuildImageSource(i + ".png", tileSize);
            }

            for (int i = 0; i < 10; i++) {
                LedValue[i] = Utility.BuildImageSource("led" + i + ".png", 24, 40);
            }

            CustomWidth.Text = "100";
            CustomHeight.Text = "100";
            CustomMines.Text = "2000";

            for (int i = 0; i < digits.Length; i++) {
                digits[i] = new Image() {
                    Source = LedValue[0]
                };
                Canvas.SetTop(digits[i], 0);
                Canvas.SetLeft(digits[i], i * LedValue[0].Width);
                MinesLeft.Children.Add(digits[i]);
            }

            // complete creating the popup
            popupText.Text = "";
            popupText.Background = new SolidColorBrush( Color.FromArgb(128, 227, 227, 227));
            popupText.Foreground = Brushes.Black;
            popupText.FontSize = 20;
            popupText.FontWeight = FontWeights.Bold;
            
            gameToolTip.Child = popupText;

            gameToolTip.AllowsTransparency = true;
            gameToolTip.Placement = PlacementMode.Relative;
            gameToolTip.PlacementTarget = gameCanvas;
            gameToolTip.IsOpen = true;

            gameCanvas.Children.Add(gameToolTip);

            // start up a game
            InitializeTiles1();

            Utility.Write("Graphics rendering tier = " + (RenderCapability.Tier >> 16));

            SolverMain.Initialise();
        }

        private void AutoPlay() {

            Thread autoplay = new Thread(AutoPlayRunner);
            autoplay.Start();

        }


        // this method runs on a different thread. gameNumber can be changed by clicking chosing a new game from the UI thread.
        private void AutoPlayRunner() {

            Dispatcher.Invoke(() => gameCanvas.Cursor = Cursors.Wait);
            logicalLock = true;

            int runningGameNumber = gameNumber;  // remember the game number we are solving

            //Utility.Write("AutoRunning thread starting");

            solverActions = SolverMain.FindActions(solverInfo);   // pass the solver info into the solver and see what it comes up with
            Dispatcher.Invoke(() => RenderHints());

            while (IsAutoPlayValid() && gameNumber == runningGameNumber) {

                long start = DateTime.Now.Ticks;

                GameResult result = game.ProcessActions(solverActions.solverActions);

                //Utility.Write("Game controller returned " + result.actionResults.Count + " results from this action");

                long end1 = DateTime.Now.Ticks;
                // do the rendering while we are stil playing the same game
                if (gameNumber == runningGameNumber) {
                    gameCanvas.Dispatcher.Invoke(() => RenderResults(result));
                } else {
                    break;
                }
                //Utility.Write("After RenderResults took " + (DateTime.Now.Ticks - start) + " ticks");

                solverInfo.AddInformation(result);  // let the solver know what has happened

                solverActions = SolverMain.FindActions(solverInfo);   // pass the solver info into the solver and see what it comes up with

                //foreach (SolverAction action in solverActions) {
                //    Utility.Write("Playing " + action.AsText() + " probability " + action.safeProbability);
                //}

                //long end2 = DateTime.Now.Ticks;
                // do the rendering while we are stil playing the same game
                if (gameNumber == runningGameNumber) {
                    Dispatcher.Invoke(() => RenderHints());
                } else {
                    break;
                }
                //Utility.Write("After RenderHints took " + (DateTime.Now.Ticks - start) + " ticks");
                Utility.Write("Tiles remaining " + solverInfo.GetTilesLeft() + ", excluded " + solverInfo.GetExcludedTiles().Count);

                long end = DateTime.Now.Ticks;

                int milliseconds = (int) ((end - start) / 10000);

                int wait = Math.Max(0, autoPlayMinDelay - milliseconds);

                //Utility.Write("Autoplay processing took " + milliseconds + " milliseconds (" + (end - start) + " ticks)");
                //Console.WriteLine("Autoplay processing took " + milliseconds + " milliseconds");

                if (!IsAutoPlayValid()) {
                    break;
                }

                Thread.Sleep(wait);  // wait until all the time is used up

            }

            logicalLock = false;
            Dispatcher.Invoke(() => gameCanvas.Cursor = Cursors.Arrow);

            //Utility.Write("AutoRunning thread ending");

        }

        // create the non-graphical parts of the game
        private void InitializeTiles1() {

            gameNumber++;
            logicalLock = false;

            minesLeftSize = Math.Max(3, (int) Math.Log10(gameDescription.mines) + 1);

            MinesLeftHolder.Width = 24 * minesLeftSize + 8;
            for (int i= 0; i < digits.Length; i++) {
                if (i < minesLeftSize) {
                    digits[i].Visibility = Visibility.Visible;
                } else {
                    digits[i].Visibility = Visibility.Hidden;
                }
            }

            int seed = 0;
            if (useSeed.IsChecked == true) {
                try {
                    seed = int.Parse(SeedTextBox.Text);
                } catch (Exception) {
                }
            }

            game = new MinesweeperGame(gameDescription, seed);
            solverInfo = new SolverInfo(gameDescription, verbose);

            RenderMinesLeft();

            long start = DateTime.Now.Ticks;

            tiles = new ActionResult[gameDescription.width, gameDescription.height];
            solverActions = new SolverActionHeader();   // remove any hints from the previous game
             
            Utility.Write("Ticks to build screen " + (DateTime.Now.Ticks - start));

        }

        // build the graphical part of the board
        private void BuildBoard() {
 
            //long start = DateTime.Now.Ticks;

            images = new ImageDrawing[boardWidth, boardHeight];

            // remove existing tiles from the canvas
            gameCanvas.Children.Clear();  // clear game board

            imageDrawings = new DrawingGroup();

            for (int y = 0; y < boardHeight; y++) {
                for (int x = 0; x < boardWidth; x++) {

                    ActionResult tile = tiles[topX + x, topY + y];
                    ImageSource imageSource;
                    if (tile == null) {
                        imageSource = Hidden;

                    } else if (tile.resultType == ResultType.Cleared) {
                        imageSource = MineValue[tile.value];

                    } else if (tile.resultType == ResultType.Flagged) {
                        imageSource = Flagged;

                    } else if (tile.resultType == ResultType.Hidden) {
                        imageSource = Hidden;

                    } else if (tile.resultType == ResultType.Exploded) {
                        imageSource = MineWrong;

                    } else if (tile.resultType == ResultType.FlaggedWrong) {
                        imageSource = FlaggedWrong;

                    } else if (tile.resultType == ResultType.Mine) {
                        imageSource = Mine;
                    } else {
                        imageSource = Hidden;
                    }


                    ImageDrawing image = new ImageDrawing() {
                        ImageSource = imageSource,
                        Rect = new Rect(x * tileSize, y * tileSize, tileSize, tileSize)
                    };

                    imageDrawings.Children.Add(image);

                    images[x, y] = image;
 
                }
            }

            totalImage = new Image() {
                Source = new DrawingImage(imageDrawings)
            };

            Canvas.SetLeft(totalImage, 0);
            Canvas.SetTop(totalImage, 0);
            gameCanvas.Children.Add(totalImage);

            // create an empty hints image which we'll use later
            hintImage = new Image();

            Canvas.SetLeft(hintImage, 0);
            Canvas.SetTop(hintImage, 0);
            gameCanvas.Children.Add(hintImage);

            //Utility.Write("Ticks to build screen " + (DateTime.Now.Ticks - start));

        }

        private void DoAction(int x, int y, ActionType action) {

            //int index = gameDescription.width * y + x;

            GameResult result = game.ProcessActions(new GameAction[] { new GameAction(x, y, action) });

            Utility.Write("Game controller returned " + result.actionResults.Count + " results from this action");

            RenderResults(result);  // draw the new board

            solverInfo.AddInformation(result);  // let the solver know what has happened

            // if we are showing hints or auto playing then get the hints
            if (useSolver || autoRunning) {
                AutoPlay();  // let the solver playout the moves
            }
 
        }

        private bool IsAutoPlayValid() {

            if (!autoRunning) {
                //Utility.Write("Autoplay not valid - AutoPlay is not checked");
                return false;
            }

            // if we have no actions then we can't autoplay
            if (solverActions.solverActions.Count == 0) {
                //Utility.Write("Autoplay not valid - no actions found by the solver");
                return false;
            }

            // if we are accepting guesses then everything goes
            if (useGuesses) {
                //Utility.Write("Autoplay valid - accepting guesses");
                return true;
            }

            if ((solverActions.solverActions[0].safeProbability > 0 && solverActions.solverActions[0].safeProbability < 1)) {
                //Utility.Write("Autoplay not valid - no certain actions found by the solver and we aren't accepting guesses");
                return false;
            }

            //Utility.Write("Autoplay valid - certain actions found by the solver");
            return true;

        }

        // draw the outputs from the Game onto the visible board
        private void RenderResults(GameResult result) {

            //long start = DateTime.Now.Ticks;

            SeedTextBox.Text = game.seed.ToString();

            hintImage.Visibility = Visibility.Hidden;  // hide the previous hints

            foreach (ActionResult ar in result.actionResults) {

                // remember what happen here last
                tiles[ar.x, ar.y] = ar;

                int boardX = ar.x - topX;
                int boardY = ar.y - topY;

                // if the action is taking place in the part of the board we are looking at
                if (boardX >= 0 && boardX < boardWidth && boardY >= 0 && boardY < boardHeight) {

                    ImageDrawing image = images[boardX, boardY];

                    if (ar.resultType == ResultType.Cleared) {
                        image.ImageSource = MineValue[ar.value];

                    } else if (ar.resultType == ResultType.Flagged) {
                        image.ImageSource = Flagged;

                    } else if (ar.resultType == ResultType.Hidden) {
                        image.ImageSource = Hidden;

                    } else if (ar.resultType == ResultType.Exploded) {
                        image.ImageSource = MineWrong;

                    } else if (ar.resultType == ResultType.FlaggedWrong) {
                        image.ImageSource = FlaggedWrong;

                    } else if (ar.resultType == ResultType.Mine) {
                        image.ImageSource = Mine;
                    }

                }

            }

            RenderMinesLeft();

            //Utility.Write("Ticks to render results " + (DateTime.Now.Ticks - start));

        }

        // draw the outputs from the Solver onto the visible board
        private void RenderHints() {

            //long start = DateTime.Now.Ticks;

            DrawingGroup hintDrawings = new DrawingGroup();

            int offSetLeft = tileSize * (boardWidth + 1);
            int offSetTop = tileSize * (boardHeight + 1);

            jumpTo = null;
            if (solverActions.solverActions.Count == 0) {
                MessageLine.Content = "The solver has no suggestions!";
            } else if (solverActions.solverActions.Count > 1) {
                MessageLine.Content = "The solver has found " + solverActions.solverActions.Count + " moves.";
            } else if (solverActions.solverActions[0].safeProbability > 0 && solverActions.solverActions[0].safeProbability < 1) {
                MessageLine.Content = "The solver suggests guessing " + solverActions.solverActions[0].AsText();
                jumpTo = solverActions.solverActions[0];
            } else {
                MessageLine.Content = "The solver has found 1 move.";
            }

            foreach (SolverAction ar in solverActions.solverActions) {

                int boardX = ar.x - topX;
                int boardY = ar.y - topY;

                // if the action is taking place in the part of the board we are looking at
                if (boardX >= 0 && boardX < boardWidth && boardY >= 0 && boardY < boardHeight) {

                    int x = boardX * tileSize;
                    int y = boardY * tileSize;

                    if (x < offSetLeft) {
                        offSetLeft = x;
                    }
                    if (y < offSetTop) {
                        offSetTop = y;
                    }

                    Color fill;
                    if (ar.action == ActionType.Clear) {
                        if (ar.safeProbability == 1) {
                            fill = Color.FromArgb(128, 0, 255, 0);  // green if safe
                        } else if (ar.isDead) {
                            if (ar.isExcluded) {
                                fill = Color.FromArgb(128, 165, 42, 42);    // if dead
                            } else {
                                fill = Color.FromArgb(128, 0, 0, 0);    // black if excluded
                            }
                            
                        } else {
                            fill = Color.FromArgb(128, 255, 165, 0);  // orange if not safe
                        }

                    } else if (ar.action == ActionType.Flag) {
                        fill = Color.FromArgb(128, 255, 0, 0);   // red if a mine
                    }

                    GeometryDrawing square =
                        new GeometryDrawing(
                            new SolidColorBrush(fill),
                            new Pen(Brushes.Black, 0),
                            new RectangleGeometry(new Rect(x, y, tileSize, tileSize))
                        );

                    hintDrawings.Children.Add(square);
                }

            }

            // render dead tiles
            foreach (SolverAction ar in solverActions.deadActions) {

                int boardX = ar.x - topX;
                int boardY = ar.y - topY;

                // if the action is taking place in the part of the board we are looking at
                if (boardX >= 0 && boardX < boardWidth && boardY >= 0 && boardY < boardHeight) {

                    int x = boardX * tileSize;
                    int y = boardY * tileSize;

                    if (x < offSetLeft) {
                        offSetLeft = x;
                    }
                    if (y < offSetTop) {
                        offSetTop = y;
                    }

                    Color fill = Color.FromArgb(128, 0, 0, 0);    // black if dead

                    GeometryDrawing square =
                        new GeometryDrawing(
                            new SolidColorBrush(fill),
                            new Pen(Brushes.Black, 0),
                            new RectangleGeometry(new Rect(x, y, tileSize, tileSize))
                        );

                    hintDrawings.Children.Add(square);
                }

            }

            // replace the old composite hints image with the new
            hintImage.Source = new DrawingImage(hintDrawings);

            // and place it in the write position
            Canvas.SetLeft(hintImage, offSetLeft);
            Canvas.SetTop(hintImage, offSetTop);
            hintImage.Visibility = Visibility.Visible;
 
            //Utility.Write("Ticks to render hints " + (DateTime.Now.Ticks - start));
        }

        private void RebuildBoard(int topX, int topY, int boardWidth, int boardHeight) {

            this.topX = topX;
            this.topY = topY;
            this.boardWidth = boardWidth;
            this.boardHeight = boardHeight;

            // make sure the top position allows enough space to fill the board
            if (this.topX + this.boardWidth > gameDescription.width) {
                this.topX = gameDescription.width - this.boardWidth;
            }

            if (this.topY + this.boardHeight > gameDescription.height) {
                this.topY = gameDescription.height - this.boardHeight;
            }


            if (boardWidth > 0 && boardHeight > 0) {
                BuildBoard();
                RenderHints();
            }

        }

        private void RenderMinesLeft() {

            int i = minesLeftSize - 1;
            int mines = game.GetMinesLeft();

            while (i >= 0) {

                int d0 = mines % 10;
                mines = (mines - d0) / 10;

                digits[i].Source = LedValue[d0];

                i--;
            }

        }

        private void CanvasMouseDown(object sender, MouseButtonEventArgs e) {


            if (logicalLock) {  // this means the solver is playing the game and the user input is ignored
                return;
            }
                 
            int x = (int) e.GetPosition(gameCanvas).X;
            int y = (int) e.GetPosition(gameCanvas).Y;

            //Utility.Write("Mouse clicked at X=" + x + ", Y=" + y);

            int col = x / tileSize;
            int row = y / tileSize;

            if (col < 0 || col > boardWidth || row < 0 || row > boardHeight) {
                Utility.Write("Clicked outside of game boundary col=" + col + ", row=" + row);
                return;
            }


            //Utility.Write("col=" + col + ", row=" + row);
            ActionType actionType;

            ActionResult tile = tiles[col + topX, row + topY];

            if (e.ChangedButton == MouseButton.Left) {
                if (tile == null || tile.resultType == ResultType.Hidden) {
                    actionType = ActionType.Clear;
                } else {
                    actionType = ActionType.Chord;
                }

            } else if (e.ChangedButton == MouseButton.Right) {
                actionType = ActionType.Flag;
            } else {
                return;
            }

            DoAction(col + topX, row + topY, actionType);

        }

        private void NewGame() {

            InitializeTiles1();

            // reposition the display to the top left
            //topX = 0;
            //topY = 0;

            // move the scroll bars back to the start
            horizontalScrollbar.Value = 0;
            verticalScrollbar.Value = 0;

            // work out if scroll bars are need and how big the thumb is
            //boardWidth = DoWidth(mainWindow.ActualWidth);
            //boardHeight = DoHeight(mainWindow.ActualHeight);

            RebuildBoard(0, 0, DoWidth(mainWindow.ActualWidth), DoHeight(mainWindow.ActualHeight));

        }

        private void BeginnerClick(object sender, RoutedEventArgs e) {

            if (zeroStart.IsChecked == true) {
                gameDescription = GameDescription.BEGINNER_ZERO;
            } else {
                gameDescription = GameDescription.BEGINNER_SAFE;
            }

            NewGame();

        }

        private void IntermediateClick(object sender, RoutedEventArgs e) {

            if (zeroStart.IsChecked == true) {
                gameDescription = GameDescription.INTERMEDIATE_ZERO;
            } else {
                gameDescription = GameDescription.INTERMEDIATE_SAFE;
            }

            NewGame();

        }

        private void ExpertClick(object sender, RoutedEventArgs e) {

            
            if (zeroStart.IsChecked == true) {
                gameDescription = GameDescription.EXPERT_ZERO;
            } else {
                gameDescription = GameDescription.EXPERT_SAFE;
            }

            NewGame();

        }

        private void CustomClick(object sender, RoutedEventArgs e) {

            int gameWidth;
            int gameHeight;
            int gameMines;

            try {
                gameWidth = int.Parse(CustomWidth.Text);
            } catch (System.Exception) {
                gameWidth = 30;
                CustomWidth.Text = gameWidth.ToString();
            }

            try {
                gameHeight = int.Parse(CustomHeight.Text);
            } catch (System.Exception) {
                gameHeight = 16;
                CustomHeight.Text = gameHeight.ToString();
            }

            try {
                gameMines = int.Parse(CustomMines.Text);
            } catch (System.Exception) {
                gameMines = gameWidth * gameHeight / 5;
                CustomMines.Text = gameMines.ToString();
            }

            if (zeroStart.IsChecked == true) {
                gameDescription = new GameDescription(gameWidth, gameHeight, gameMines, GameType.Zero);
            } else {
                gameDescription = new GameDescription(gameWidth, gameHeight, gameMines, GameType.Safe);
            }

            NewGame();

        }

        // redraws the controls based on the new width and returns the number of columns we are now able to show
        private int DoWidth(double width) {

            MessageHolder.Width = Math.Max(0, width - 260);

            double boardMax = gameDescription.width * tileSize + 8;

            double actualWidth = Math.Max(8, Math.Min(boardMax, width - 260));

            int showTilesWidth = (int)Math.Floor((actualWidth - 8) / tileSize);

            double boardWidthPixels = showTilesWidth * tileSize + 8;  // board is in Whole tiles

            BoardHolder.Width = boardWidthPixels;

            if (showTilesWidth == gameDescription.width) {
                horizontalScrollbar.Visibility = Visibility.Hidden;
            } else {
                horizontalScrollbar.Visibility = Visibility.Visible;
            }

            horizontalScrollbar.Width = boardWidthPixels;
            horizontalScrollbar.Maximum = (gameDescription.width - showTilesWidth);
            horizontalScrollbar.ViewportSize = showTilesWidth;
            horizontalScrollbar.LargeChange = showTilesWidth;  // scroll bar large scroll is a whole screen

            return showTilesWidth;

        }

        // redraws the controls based on the new height and returns the number of columns we are now able to show
        private int DoHeight(double height) {

            double boardMax = gameDescription.height * tileSize + 8;

            double actualHeight = Math.Max(8, Math.Min(boardMax, height - 140));

            int showTilesHeight = (int) Math.Floor((actualHeight - 8) / tileSize);

            double boardHeightPixels = showTilesHeight * tileSize + 8;  // board is in Whole tiles

            BoardHolder.Height = boardHeightPixels;

            if (showTilesHeight == gameDescription.height) {
                verticalScrollbar.Visibility = Visibility.Hidden;
            } else {
                verticalScrollbar.Visibility = Visibility.Visible;
            }
 
            verticalScrollbar.Height = boardHeightPixels;
            verticalScrollbar.Maximum = (gameDescription.height - showTilesHeight);
            verticalScrollbar.ViewportSize = showTilesHeight;
            verticalScrollbar.LargeChange = showTilesHeight;  // scroll bar large scroll is a whole screen

            return showTilesHeight;
 
        }


        private void RedrawWindow(object sender, SizeChangedEventArgs e) {

            //Utility.Write("New width:" + e.NewSize.Width + " height:" + e.NewSize.Height);

            int newBoardWidth = boardWidth;
            int newBoardHeight = boardHeight;

            if (e.WidthChanged == true) {
                newBoardWidth = DoWidth(e.NewSize.Width);
            } 

            if (e.HeightChanged == true) {
                newBoardHeight = DoHeight(e.NewSize.Height);
            }

            if (newBoardWidth != this.boardWidth || newBoardHeight != this.boardHeight) {
                RebuildBoard(topX, topY, newBoardWidth, newBoardHeight);
            }

        }

        private void VerticalScroll(object sender, System.Windows.Controls.Primitives.ScrollEventArgs e) {

            //Utility.Write("Vertical scroll : " + e.NewValue);

            int newTopY = (int) Math.Floor(e.NewValue + 0.5d);

            if (newTopY != topY) {
                RebuildBoard(topX, newTopY, boardWidth, boardHeight);
            }

        }

        private void HorizontalScroll(object sender, System.Windows.Controls.Primitives.ScrollEventArgs e) {

            //Utility.Write("Horizontal scroll : " + e.NewValue);

            int newTopX = (int)Math.Floor(e.NewValue + 0.5d);

            if (newTopX != topX) {
                RebuildBoard(newTopX, topY, boardWidth, boardHeight);
            }

        }

        private void MouseMoveOnBoard(object sender, MouseEventArgs e) {

            int x = (int)e.GetPosition(gameCanvas).X;
            int y = (int)e.GetPosition(gameCanvas).Y;

            int col = x / tileSize;
            int row = y / tileSize;

            if (col < 0 || col > boardWidth || row < 0 || row > boardHeight) {
                Utility.Write("Mouse outside of game boundary col=" + col + ", row=" + row);
                return;
            }

            int realCol = col + topX;
            int realRow = row + topY;

            double prob = solverInfo.GetProbability(realCol, realRow);
            string probText;
            if (prob == -1) {
                probText = "";
            } else if (prob == 0) {
                probText = "Mine";
            } else if (prob == 1) {
                 probText = "Clear";
            } else {
                //probText = String.Format("0.00", prob) + " Safe";
                probText = (prob * 100).ToString("N2") + "% Safe";
            }

            popupText.Text = "(" + realCol + "," + realRow + ") " + probText;

            gameToolTip.HorizontalOffset = x;
            gameToolTip.VerticalOffset = y + 10;

        }

        private void MouseLeftBoard(object sender, MouseEventArgs e) {

            gameToolTip.IsOpen = false;

        }

        private void MouseEnteredBoard(object sender, MouseEventArgs e) {

            gameToolTip.IsOpen = true;

        }

        private void ClickMessageLine(object sender, MouseButtonEventArgs e) {

            if (jumpTo == null) {
                return;
            }

            // set the scroll bars
            verticalScrollbar.Value = jumpTo.y - this.boardHeight / 2;
            horizontalScrollbar.Value = jumpTo.x - this.boardWidth / 2;

            // now see where the scrollbars are 
            int newTopY = (int)Math.Floor(verticalScrollbar.Value + 0.5d);
            int newTopX = (int)Math.Floor(horizontalScrollbar.Value + 0.5d);

            // redraw board if we need to 
            if (newTopY != topY || newTopX != topX) {
                RebuildBoard(newTopX, newTopY, boardWidth, boardHeight);
            }


        }

        private void MouseWheelOnBoard(object sender, MouseWheelEventArgs e) {

            // if shift is pressed scroll left and right
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) {
                horizontalScrollbar.Value -= e.Delta / 40;

                int newTopX = (int)Math.Floor(horizontalScrollbar.Value + 0.5d);

                if (newTopX != topX) {
                    RebuildBoard(newTopX, topY, boardWidth, boardHeight);
                }

                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) {
                return;
            }

            verticalScrollbar.Value -= e.Delta / 40;

            int newTopY = (int)Math.Floor(verticalScrollbar.Value + 0.5d);

            if (newTopY != topY) {
                RebuildBoard(topX, newTopY, boardWidth, boardHeight);
            }

        }

        private void SetSolverDetails(object sender, RoutedEventArgs e) {

            useSolver = (showHInts.IsChecked == true);
            autoRunning = (autoPlay.IsChecked == true);
            useGuesses = (acceptGuesses.IsChecked == true);

            //Console.WriteLine(useSolver + " " + autoRunning + " " + useGuesses);

        }
    }
}
