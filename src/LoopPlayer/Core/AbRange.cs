namespace LoopPlayer.Core;

/// <summary>
/// Точки A и B (в секундах). Всегда выполняется 0 ≤ A ≤ B ≤ Duration.
/// </summary>
public sealed class AbRange
{
    public const double SmallStep = 0.2;
    public const double LargeStep = 1.0;

    public double Duration { get; private set; }
    public double A { get; private set; }
    public double B { get; private set; }

    /// <summary>Вызывается при любом изменении A или B (не при смене трека).</summary>
    public event Action? Changed;

    public double Length => B - A;

    /// <summary>Сброс под новый трек: A = 0, B = длительность.</summary>
    public void ResetForDuration(double duration)
    {
        Duration = Math.Max(0, duration);
        A = 0;
        B = Duration;
    }

    /// <summary>Восстановление сохранённой пары A/B с приведением к допустимому диапазону.</summary>
    public void Restore(double a, double b)
    {
        a = Clamp(a);
        b = Math.Max(Clamp(b), a);
        Apply(a, b);
    }

    /// <summary>Выход из цикла: B = длительность, A остаётся на месте.</summary>
    public void ReleaseB() => Apply(A, Duration);

    /// <summary>Кнопка/клавиша A: A = позиция; если она правее B — B подтягивается к A.</summary>
    public void SetAFromPosition(double position)
    {
        var a = Clamp(position);
        var b = Math.Max(B, a);
        Apply(a, b);
    }

    /// <summary>Кнопка/клавиша B: B = позиция; если она левее A — A подтягивается к B.</summary>
    public void SetBFromPosition(double position)
    {
        var b = Clamp(position);
        var a = Math.Min(A, b);
        Apply(a, b);
    }

    /// <summary>Сдвиг A на delta с ограничением [0, B] — B не двигается.</summary>
    public void NudgeA(double delta) => MoveA(A + delta);

    /// <summary>Сдвиг B на delta с ограничением [A, Duration] — A не двигается.</summary>
    public void NudgeB(double delta) => MoveB(B + delta);

    /// <summary>Перетаскивание шкалы A: A = значение с ограничением [0, B].</summary>
    public void MoveA(double value) => Apply(Math.Clamp(Clamp(value), 0, B), B);

    /// <summary>Перетаскивание шкалы B: B = значение с ограничением [A, Duration].</summary>
    public void MoveB(double value) => Apply(A, Math.Clamp(Clamp(value), A, Duration));

    private double Clamp(double v) => Math.Clamp(double.IsNaN(v) ? 0 : v, 0, Duration);

    private void Apply(double a, double b)
    {
        if (a == A && b == B) return;
        A = a;
        B = b;
        Changed?.Invoke();
    }
}
