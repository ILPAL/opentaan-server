package io.opentaan.admin.economy;

/** What an operator asks for: change one character's gold, and why. */
public record AdjustmentRequestCommand(long characterId, long deltaGold, String reason) {
}
