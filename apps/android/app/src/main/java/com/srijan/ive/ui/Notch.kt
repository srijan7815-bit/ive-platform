package com.srijan.ive.ui

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/**
 * The Dynamic-Island-style notch: one surface that springs between idle, working
 * and approval states. Width adapts to the screen (phones, foldables, tablets).
 */
@Composable
fun Notch(
    phase: Phase,
    approval: ApprovalUi?,
    running: Int,
    needsKey: Boolean,
    onApprove: () -> Unit,
    onDeny: () -> Unit,
    onTap: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val haptic = LocalHapticFeedback.current
    var lastApproval by remember { mutableStateOf<ApprovalUi?>(null) }
    LaunchedEffect(approval) {
        if (approval != null) {
            lastApproval = approval
            haptic.performHapticFeedback(HapticFeedbackType.LongPress)
        }
    }

    BoxWithConstraints(modifier.fillMaxWidth(), contentAlignment = Alignment.TopCenter) {
        val maxW = minOf(maxWidth - 24.dp, 420.dp)
        val expanded = approval != null
        val working = phase is Phase.Thinking || phase is Phase.Working

        val spec = spring<Dp>(dampingRatio = 0.62f, stiffness = Spring.StiffnessMediumLow)
        val w by animateDpAsState(if (expanded) maxW else if (working) minOf(maxW, 280.dp) else 176.dp, spec, label = "w")
        val h by animateDpAsState(if (expanded) 196.dp else 38.dp, spec, label = "h")
        val r by animateDpAsState(if (expanded) 30.dp else 19.dp, spec, label = "r")

        val label = when {
            expanded -> "Needs your approval"
            phase is Phase.Thinking -> "Thinking…"
            phase is Phase.Working -> phase.text
            phase is Phase.Done -> "Done ✓"
            phase is Phase.Error -> "Something went wrong"
            needsKey -> "Add your API key"
            else -> "IVE"
        }
        val dotColor by animateColorAsState(
            when {
                expanded -> Ive.warn
                phase is Phase.Error -> Ive.bad
                working -> Ive.accent
                else -> Ive.ok
            },
            label = "dot",
        )
        val pulse by rememberInfiniteTransition(label = "pulse").animateFloat(
            initialValue = 0.8f,
            targetValue = 1.35f,
            animationSpec = infiniteRepeatable(tween(if (working || expanded) 600 else 1300), RepeatMode.Reverse),
            label = "pulse",
        )

        val shape = RoundedCornerShape(r)
        Column(
            Modifier
                .width(w)
                .height(h)
                .clip(shape)
                .background(Color(0xF2050507), shape)
                .border(1.dp, Color.White.copy(alpha = 0.1f), shape)
                .clickable(enabled = !expanded, onClick = onTap)
                .padding(horizontal = 18.dp),
        ) {
            Row(Modifier.fillMaxWidth().height(38.dp), verticalAlignment = Alignment.CenterVertically) {
                Spacer(Modifier.size(8.dp).scale(pulse).background(dotColor, CircleShape))
                Spacer(Modifier.width(10.dp))
                Text(
                    label, color = Ive.ink, fontSize = 13.sp, fontWeight = FontWeight.SemiBold,
                    maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f),
                )
                if (running > 1) Text("$running", color = Ive.accent, fontSize = 12.sp, fontWeight = FontWeight.Bold)
            }
            AnimatedVisibility(visible = expanded, enter = fadeIn(tween(260, delayMillis = 140)), exit = fadeOut(tween(110))) {
                val a = approval ?: lastApproval
                if (a != null) {
                    Column {
                        Text(a.title, color = Ive.ink, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, maxLines = 2, overflow = TextOverflow.Ellipsis)
                        Spacer(Modifier.height(8.dp))
                        Text(
                            a.detail, color = Ive.ink2, fontSize = 11.sp, fontFamily = FontFamily.Monospace, maxLines = 4, overflow = TextOverflow.Ellipsis,
                            modifier = Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).background(Color.White.copy(alpha = 0.07f)).padding(10.dp),
                        )
                        Spacer(Modifier.height(12.dp))
                        Row(horizontalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(10.dp)) {
                            PillButton("Deny", Modifier.weight(1f), onClick = onDeny)
                            PillButton("Approve", Modifier.weight(1f), fill = Ive.ok, textColor = Color(0xFF04210F), onClick = onApprove)
                        }
                    }
                }
            }
        }
    }
}
