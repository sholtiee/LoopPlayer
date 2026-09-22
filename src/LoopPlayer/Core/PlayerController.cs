using LoopPlayer.Audio;

namespace LoopPlayer.Core;

/// <summary>
/// Фасад плеера: единая точка входа для кнопок и горячих клавиш.
/// Связывает движок, скорость, точки A/B и счётчик повторений.
/// </summary>
public sealed class PlayerController : IDisposable
{
    private readonly AudioEngine _engine = new();
    private CancellationTokenSource? _loadCts;
    private bool _jumpToAOnRangeChange = true;

    public PlayerController()
    {
        Speed.Changed += () => _engine.Speed = Speed.Speed;
        Range.Changed += OnRangeChanged;
        _engine.Ended += () => StateChanged?.Invoke();
        _engine.Error += message => Error?.Invoke(message);
    }

    public SpeedController Speed { get; } = new();
    public AbRange Range { get; } = new();
    public RepeatCounter Counter { get; } = new();

    public Track? Track => _engine.Track;
    public bool HasTrack => _engine.Track is not null;
    public bool IsLoading { get; private set; }
    public bool IsPlaying => _engine.IsPlaying;
    public double Position => _engine.Position;
    public double Duration => _engine.Duration;

    /// <summary>Изменилось состояние Play/Pause, трек или началась/закончилась загрузка.</summary>
    public event Action? StateChanged;

    /// <summary>Сообщение об ошибке для показа пользователю.</summary>
    public event Action<string>? Error;

    // ---------- Загрузка ----------

    public async Task LoadTrackAsync(string path)
    {
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        IsLoading = true;
        StateChanged?.Invoke();

        Track track;
        try
        {
            track = await Task.Run(() => TrackLoader.Load(path, cts.Token), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (TrackLoadException ex)
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                IsLoading = false;
                StateChanged?.Invoke();
                Error?.Invoke(ex.Message);
            }
            return;
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                IsLoading = false;
                StateChanged?.Invoke();
                Error?.Invoke("Не удалось загрузить файл:\n" + ex.Message);
            }
            return;
        }

        if (!ReferenceEquals(_loadCts, cts)) return; // начата загрузка другого файла

        // Полный сброс состояния предыдущего трека.
        _engine.Load(track);
        Range.ResetForDuration(track.DurationSeconds);
        _engine.SetLoop(Range.A, Range.B);
        Counter.Reset();

        IsLoading = false;
        StateChanged?.Invoke();
    }

    // ---------- Воспроизведение ----------

    public void TogglePlayPause()
    {
        if (!HasTrack) return;
        Guard(() => _engine.TogglePlayPause());
        StateChanged?.Invoke();
    }

    /// <summary>Шаговая перемотка (±0,2 / ±1 / ±2): не выводит позицию за пределы фрагмента A–B.</summary>
    public void SeekBy(double deltaSeconds)
    {
        if (!HasTrack) return;
        var target = PositionController.SeekBy(Position, deltaSeconds, Duration);
        SeekTo(Math.Clamp(target, Range.A, Range.B));
    }

    public void SeekTo(double seconds)
    {
        if (!HasTrack) return;
        Guard(() => _engine.Seek(PositionController.Clamp(seconds, Duration)));
        StateChanged?.Invoke();
    }

    /// <summary>В начало трека; состояние Play/Pause не меняется.</summary>
    public void GoToStart() => SeekTo(0);

    /// <summary>Точно в A; состояние Play/Pause не меняется.</summary>
    public void GoToA() => SeekTo(Range.A);

    // ---------- Точки A и B ----------

    public void SetA()
    {
        if (!HasTrack) return;
        // Позиция и так становится точкой A — переход не нужен.
        WithoutJumpToA(() => Range.SetAFromPosition(Position));
    }

    public void SetB()
    {
        if (!HasTrack) return;
        Range.SetBFromPosition(Position);
    }

    public void NudgeA(double delta)
    {
        if (HasTrack) Range.NudgeA(delta);
    }

    public void NudgeB(double delta)
    {
        if (HasTrack) Range.NudgeB(delta);
    }

    public void MoveA(double seconds)
    {
        if (HasTrack) Range.MoveA(seconds);
    }

    public void MoveB(double seconds)
    {
        if (HasTrack) Range.MoveB(seconds);
    }

    /// <summary>
    /// Выход из цикла: B = длительность, A остаётся на месте.
    /// Границы применяются к аудиопотоку сразу, а позиция не меняется — воспроизведение
    /// продолжается за прежнюю точку B до конца записи.
    /// </summary>
    public void ExitLoop()
    {
        if (HasTrack) WithoutJumpToA(Range.ReleaseB);
    }

    /// <summary>
    /// После изменения A или B воспроизведение переходит в точку A (Play/Pause сохраняется),
    /// чтобы новый фрагмент сразу слушался с начала. Исключения оформляются через WithoutJumpToA.
    /// </summary>
    private void OnRangeChanged()
    {
        _engine.SetLoop(Range.A, Range.B);
        Counter.Reset();
        if (_jumpToAOnRangeChange) SeekTo(Range.A);
    }

    private void WithoutJumpToA(Action action)
    {
        _jumpToAOnRangeChange = false;
        try { action(); }
        finally { _jumpToAOnRangeChange = true; }
    }

    // ---------- Сохранение состояния ----------

    public AppSettings SnapshotSettings() => new()
    {
        TrackPath = Track?.Path,
        A = Range.A,
        B = Range.B,
        Position = Position,
        SpeedSteps = Speed.Steps,
    };

    /// <summary>Применяет сохранённые A/B, скорость и позицию к уже загруженному треку.</summary>
    public void RestoreSettings(AppSettings settings)
    {
        Speed.Steps = settings.SpeedSteps;
        if (!HasTrack) return;
        WithoutJumpToA(() => Range.Restore(settings.A, settings.B));
        SeekTo(settings.Position);
    }

    public void ResetCounter() => Counter.Reset();

    // ---------- Скорость ----------

    public void SpeedUp() => Speed.Increase();
    public void SpeedDown() => Speed.Decrease();

    // ---------- Периодическое обновление ----------

    /// <summary>Вызывается таймером UI: переносит переходы B→A из аудиопотока в счётчик.</summary>
    public void Tick()
    {
        var wraps = _engine.TakeWraps();
        if (wraps > 0) Counter.Add(wraps);
    }

    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (AudioEngineException ex)
        {
            Error?.Invoke(ex.Message);
        }
    }

    public void Dispose()
    {
        _loadCts?.Cancel();
        _engine.Dispose();
    }
}
