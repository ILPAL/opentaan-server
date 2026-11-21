namespace OpenTaan.GameServer.Characters;

/// <summary>The experience at which a level begins, and at which the next level begins, which the client draws its bar between.</summary>
/// <param name="Floor">The total at which the level begins.</param>
/// <param name="Ceiling">The total at which the next level begins. Always above the floor: the client divides by the difference.</param>
public readonly record struct ExperienceBar(uint Floor, uint Ceiling);
