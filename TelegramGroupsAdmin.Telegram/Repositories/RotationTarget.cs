namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// What a <see cref="RotationBag"/> resolves to: the table it rotates, and the advisory lock
/// serializing that table's cycle exhaustion. The two travel together so they cannot drift apart.
/// </summary>
/// <param name="Table">Table name, interpolated into the rotation SQL. Only ever one of
/// <see cref="RotationCycleClaim"/>'s own constants — never a caller-supplied value.</param>
/// <param name="AdvisoryLockKey">Key from <see cref="Data.Constants.AdvisoryLockKeys"/>.</param>
internal sealed record RotationTarget(string Table, long AdvisoryLockKey);
