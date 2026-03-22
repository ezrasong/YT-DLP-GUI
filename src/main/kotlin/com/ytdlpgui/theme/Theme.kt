package com.ytdlpgui.theme

import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

object AppColors {
    val Blue = Color(0xFF3478F6)
    val Background = Color(0xFF111111)
    val Surface = Color(0xFF1C1C1E)
    val SurfaceVariant = Color(0xFF2C2C2E)
    val InputField = Color(0xFF2A2A2C)
    val OnBackground = Color(0xFFFFFFFF)
    val OnSurface = Color(0xFFFFFFFF)
    val OnSurfaceVariant = Color(0xFF8E8E93)
    val Outline = Color(0xFF3A3A3C)
    val Success = Color(0xFF30D158)
    val Warning = Color(0xFFFF9F0A)
    val Error = Color(0xFFFF453A)
}

private val DarkScheme = darkColorScheme(
    primary = AppColors.Blue,
    onPrimary = Color.White,
    primaryContainer = Color(0xFF1A3A6B),
    onPrimaryContainer = AppColors.Blue,
    background = AppColors.Background,
    onBackground = AppColors.OnBackground,
    surface = AppColors.Surface,
    onSurface = AppColors.OnSurface,
    surfaceVariant = AppColors.SurfaceVariant,
    onSurfaceVariant = AppColors.OnSurfaceVariant,
    outline = AppColors.Outline,
    outlineVariant = Color(0xFF2C2C2E),
    error = AppColors.Error,
    onError = Color.White,
    errorContainer = Color(0xFF3D1212),
    onErrorContainer = AppColors.Error,
)

@Composable
fun AppTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = DarkScheme,
        typography = Typography(
            displayLarge = TextStyle(fontSize = 32.sp, fontWeight = FontWeight.Bold),
            headlineLarge = TextStyle(fontSize = 28.sp, fontWeight = FontWeight.Bold),
            headlineMedium = TextStyle(fontSize = 22.sp, fontWeight = FontWeight.Bold),
            headlineSmall = TextStyle(fontSize = 18.sp, fontWeight = FontWeight.SemiBold),
            titleLarge = TextStyle(fontSize = 18.sp, fontWeight = FontWeight.SemiBold),
            titleMedium = TextStyle(fontSize = 16.sp, fontWeight = FontWeight.Medium),
            titleSmall = TextStyle(fontSize = 14.sp, fontWeight = FontWeight.Medium),
            bodyLarge = TextStyle(fontSize = 15.sp, fontWeight = FontWeight.Normal),
            bodyMedium = TextStyle(fontSize = 14.sp, fontWeight = FontWeight.Normal),
            bodySmall = TextStyle(fontSize = 12.sp, fontWeight = FontWeight.Normal),
            labelLarge = TextStyle(fontSize = 14.sp, fontWeight = FontWeight.Medium),
            labelMedium = TextStyle(fontSize = 12.sp, fontWeight = FontWeight.Medium),
            labelSmall = TextStyle(fontSize = 11.sp, fontWeight = FontWeight.Medium),
        ),
        shapes = Shapes(
            extraSmall = RoundedCornerShape(6.dp),
            small = RoundedCornerShape(10.dp),
            medium = RoundedCornerShape(14.dp),
            large = RoundedCornerShape(18.dp),
            extraLarge = RoundedCornerShape(24.dp),
        ),
        content = content
    )
}
