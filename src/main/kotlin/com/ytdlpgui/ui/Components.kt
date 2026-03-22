package com.ytdlpgui.ui

import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.ytdlpgui.model.DownloadState
import com.ytdlpgui.theme.AppColors

@Composable
fun Card(
    modifier: Modifier = Modifier,
    content: @Composable ColumnScope.() -> Unit
) {
    Surface(
        modifier = modifier.fillMaxWidth(),
        shape = RoundedCornerShape(18.dp),
        color = MaterialTheme.colorScheme.surface,
    ) {
        Column(modifier = Modifier.padding(20.dp), content = content)
    }
}

@Composable
fun <T> FullWidthDropdown(
    label: String,
    selected: T,
    options: List<T>,
    displayText: (T) -> String,
    onSelected: (T) -> Unit,
    modifier: Modifier = Modifier
) {
    var expanded by remember { mutableStateOf(false) }

    Column(modifier = modifier) {
        Text(
            text = label,
            style = MaterialTheme.typography.labelMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(bottom = 8.dp)
        )

        Box {
            Surface(
                onClick = { expanded = true },
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(12.dp),
                color = AppColors.InputField,
            ) {
                Row(
                    modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 14.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween
                ) {
                    Text(displayText(selected), style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurface)
                    Icon(Icons.Default.KeyboardArrowDown, null, tint = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.size(20.dp))
                }
            }

            DropdownMenu(
                expanded = expanded,
                onDismissRequest = { expanded = false },
                modifier = Modifier.background(AppColors.SurfaceVariant)
            ) {
                options.forEach { option ->
                    DropdownMenuItem(
                        text = {
                            Text(
                                displayText(option),
                                style = MaterialTheme.typography.bodyMedium,
                                color = if (option == selected) AppColors.Blue else MaterialTheme.colorScheme.onSurface
                            )
                        },
                        onClick = { onSelected(option); expanded = false }
                    )
                }
            }
        }
    }
}

@Composable
fun ToggleRow(
    label: String,
    checked: Boolean,
    onCheckedChange: (Boolean) -> Unit,
    modifier: Modifier = Modifier
) {
    Row(
        modifier = modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(10.dp))
            .clickable { onCheckedChange(!checked) }
            .padding(horizontal = 4.dp, vertical = 10.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(label, style = MaterialTheme.typography.bodyLarge, color = MaterialTheme.colorScheme.onSurface)
        Switch(
            checked = checked,
            onCheckedChange = onCheckedChange,
            colors = SwitchDefaults.colors(
                checkedThumbColor = Color.White,
                checkedTrackColor = AppColors.Blue,
                uncheckedThumbColor = Color(0xFF636366),
                uncheckedTrackColor = Color(0xFF39393D),
                uncheckedBorderColor = Color.Transparent
            )
        )
    }
}

@Composable
fun ProgressBar(
    progress: Float,
    modifier: Modifier = Modifier,
    color: Color = AppColors.Blue,
    trackColor: Color = AppColors.InputField
) {
    val animatedProgress by animateFloatAsState(
        targetValue = progress.coerceIn(0f, 1f),
        animationSpec = tween(100)
    )
    Box(
        modifier = modifier.fillMaxWidth().height(6.dp).clip(RoundedCornerShape(3.dp)).background(trackColor)
    ) {
        Box(modifier = Modifier.fillMaxHeight().fillMaxWidth(animatedProgress).clip(RoundedCornerShape(3.dp)).background(color))
    }
}

@Composable
fun StatusBadge(state: DownloadState) {
    val (bgColor, textColor) = when (state) {
        DownloadState.QUEUED -> AppColors.SurfaceVariant to AppColors.OnSurfaceVariant
        DownloadState.FETCHING -> AppColors.Blue.copy(alpha = 0.15f) to AppColors.Blue
        DownloadState.DOWNLOADING -> AppColors.Blue.copy(alpha = 0.15f) to AppColors.Blue
        DownloadState.PROCESSING -> AppColors.Blue.copy(alpha = 0.15f) to AppColors.Blue
        DownloadState.FINISHED -> AppColors.Success.copy(alpha = 0.15f) to AppColors.Success
        DownloadState.ERROR -> AppColors.Error.copy(alpha = 0.15f) to AppColors.Error
        DownloadState.CANCELLED -> AppColors.SurfaceVariant to AppColors.OnSurfaceVariant
    }
    Surface(shape = RoundedCornerShape(8.dp), color = bgColor) {
        Text(state.label, style = MaterialTheme.typography.labelSmall, color = textColor, modifier = Modifier.padding(horizontal = 10.dp, vertical = 4.dp))
    }
}
