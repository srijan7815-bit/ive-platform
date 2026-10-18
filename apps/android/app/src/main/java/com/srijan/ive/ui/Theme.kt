package com.srijan.ive.ui

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

object Ive {
    val bg = Color(0xFF07080D)
    val ink = Color(0xF2FFFFFF)
    val ink2 = Color(0xA6FFFFFF)
    val ink3 = Color(0x66FFFFFF)
    val accent = Color(0xFF7DD3FC)
    val accent2 = Color(0xFFA78BFA)
    val ok = Color(0xFF4ADE80)
    val warn = Color(0xFFFBBF24)
    val bad = Color(0xFFF87171)
}

@Composable
fun IveTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = darkColorScheme(
            primary = Ive.accent,
            secondary = Ive.accent2,
            background = Ive.bg,
            surface = Ive.bg,
            onPrimary = Ive.bg,
            onBackground = Color.White,
            onSurface = Color.White,
        ),
        content = content,
    )
}
