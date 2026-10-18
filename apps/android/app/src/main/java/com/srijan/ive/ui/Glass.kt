package com.srijan.ive.ui

import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlin.math.PI
import kotlin.math.cos
import kotlin.math.sin

/** Frosted "liquid glass" surface: translucent fill, specular rim, soft depth. */
fun Modifier.glass(shape: Shape = RoundedCornerShape(24.dp), strong: Boolean = false): Modifier = this
    .clip(shape)
    .background(
        Brush.linearGradient(listOf(Color.White.copy(alpha = if (strong) 0.24f else 0.15f), Color.White.copy(alpha = 0.05f))),
        shape,
    )
    .border(
        1.dp,
        Brush.linearGradient(listOf(Color.White.copy(alpha = 0.65f), Color.White.copy(alpha = 0.06f), Color.White.copy(alpha = 0.3f))),
        shape,
    )

/** Slow-drifting aurora orbs behind everything — the "liquid" in liquid glass. */
@Composable
fun LiquidBackground() {
    val transition = rememberInfiniteTransition(label = "orbs")
    val t by transition.animateFloat(
        initialValue = 0f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(tween(26_000, easing = LinearEasing), RepeatMode.Restart),
        label = "t",
    )
    Canvas(Modifier.fillMaxSize().background(Ive.bg)) {
        val a = (t * 2.0 * PI).toFloat()
        fun orb(color: Color, cx: Float, cy: Float, r: Float) {
            val c = Offset(cx, cy)
            drawCircle(
                brush = Brush.radialGradient(colors = listOf(color.copy(alpha = 0.5f), Color.Transparent), center = c, radius = r),
                radius = r,
                center = c,
            )
        }
        orb(Color(0xFF3B82F6), size.width * (0.2f + 0.12f * sin(a)), size.height * (0.16f + 0.07f * cos(a)), size.width * 0.95f)
        orb(Color(0xFF8B5CF6), size.width * (0.85f + 0.1f * cos(a + 1f)), size.height * (0.46f + 0.09f * sin(a + 2f)), size.width * 0.9f)
        orb(Color(0xFF06B6D4), size.width * (0.4f + 0.15f * sin(a + 4f)), size.height * (0.97f + 0.04f * cos(a)), size.width * 0.95f)
    }
}

@Composable
fun PillButton(text: String, modifier: Modifier = Modifier, fill: Color? = null, textColor: Color = Ive.ink, onClick: () -> Unit) {
    val shape = RoundedCornerShape(50)
    Box(
        modifier
            .clip(shape)
            .background(fill ?: Color.White.copy(alpha = 0.14f), shape)
            .clickable(onClick = onClick)
            .padding(horizontal = 18.dp, vertical = 10.dp),
        contentAlignment = Alignment.Center,
    ) { Text(text, color = textColor, fontSize = 13.sp, fontWeight = FontWeight.SemiBold) }
}

@Composable
fun GlyphButton(glyph: String, modifier: Modifier = Modifier, diameter: Int = 44, onClick: () -> Unit) {
    Box(
        modifier.size(diameter.dp).glass(CircleShape).clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) { Text(glyph, color = Ive.ink, fontSize = (diameter * 0.42f).sp) }
}
