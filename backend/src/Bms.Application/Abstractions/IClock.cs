namespace Bms.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
