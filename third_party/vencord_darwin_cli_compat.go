//go:build cli && darwin

/*
 * SPDX-License-Identifier: GPL-3.0
 * Compatibility stub for the Vencord Installer CLI on macOS.
 */

package main

// ParseDiscordNew handles Linux's alternate install layout upstream. macOS
// uses ParseDiscord and needs this stub only because the shared CLI references it.
func ParseDiscordNew(_ string, _ string, _ bool) *DiscordInstall {
	return nil
}
