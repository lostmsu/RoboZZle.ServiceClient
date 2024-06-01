namespace RoboZZle.WebService;

public static class Converters {
    /// <summary>
    /// Converts <see cref="LevelInfo2"/> to <see cref="Puzzle"/> representation
    /// </summary>
    public static Puzzle ToPuzzle(this LevelInfo2 level) {
        var state = new PuzzleState {
            Robot = new() {
                X = level.RobotCol,
                Y = level.RobotRow,
                Direction = new Direction(level.RobotDir),
            },
        };
        ConvertCells(level, state.Cells);
        return new Puzzle {
            ID = level.Id,
            About = level.About,
            Title = level.Title,
            InitialState = state,
            CommandSet = (CommandSet)level.AllowedCommands,
            SubLengths = ConvertLengths(level.SubLengths),
            Author = level.SubmittedBy,
            SubmittedDate = level.SubmittedDate,
            // Social properties
            Difficulty = AverageDifficulty(level),
            DifficultyVoteCount = level.DifficultyVoteCount,
            Liked = level.Liked,
            Disliked = level.Disliked,
            // Filter properties
            Featured = level.Featured,
        };
    }

    #region Puzzle Helpers

    public static void ConvertCells(LevelInfo2 level, PuzzleCell[][] cells) {
        for (int y = 0; y < Puzzle.HEIGHT; y++)
        for (int x = 0; x < Puzzle.WIDTH; x++) {
            bool colored = level.Items[y][x] != LevelInfo2.VOID;
            var color = colored ? ConvertColor(level.Colors[y][x]) : null;
            bool star = level.Items[y][x] == LevelInfo2.STAR;
            cells[x][y] = new PuzzleCell { Color = color, Star = star };
        }
    }

    static int[] ConvertLengths(IList<int> subLengths) {
        for (int i = subLengths.Count - 1; i >= 0; i--) {
            if (subLengths[i] != 0)
                return subLengths.Take(i + 1).ToArray();
        }

        return [];
    }

    static Color? ConvertColor(char color) {
        return color switch {
            LevelInfo2.RED => Color.RED,
            LevelInfo2.GREEN => Color.GREEN,
            LevelInfo2.BLUE => Color.BLUE,
            _ => null,
        };
    }

    static int? AverageDifficulty(LevelInfo2 level) {
        if (level.DifficultyVoteCount > 10)
            return level.DifficultyVoteSum * 20 / level.DifficultyVoteCount;

        return null;
    }

    #endregion
}