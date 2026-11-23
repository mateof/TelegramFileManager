// Strings of the media feature: audio player, queue and viewers (owned by that feature)
const player: Record<string, string> = {
  // engine
  'Could not play "{title}"': 'No se pudo reproducir «{title}»',
  'The track could not be played': 'No se pudo reproducir la pista',
  '{count} tracks will play next': '{count} pistas sonarán a continuación',
  '{count} tracks added to the queue': '{count} pistas añadidas a la cola',
  'Sleep timer: playback paused': 'Temporizador: reproducción en pausa',

  // controls
  'Play': 'Reproducir',
  'Pause': 'Pausa',
  'Shuffle on': 'Aleatorio activado',
  'Shuffle off': 'Aleatorio desactivado',
  'Repeat off': 'Repetir desactivado',
  'Repeat all': 'Repetir todo',
  'Repeat one': 'Repetir una',
  'Previous': 'Anterior',
  'Next': 'Siguiente',
  'Mute': 'Silenciar',
  'Unmute': 'Activar sonido',
  'Volume': 'Volumen',
  'Seek': 'Posición',
  'Sleep timer': 'Temporizador de apagado',
  '{count} minutes': '{count} minutos',
  'End of track': 'Al acabar la pista',
  'Turn off the timer': 'Desactivar el temporizador',
  'More options': 'Más opciones',
  'Add to playlist': 'Añadir a una lista',
  'Save queue as playlist': 'Guardar la cola como lista',
  'Crossfade': 'Fundido entre pistas',
  'Off (gapless)': 'Desactivado (sin pausas)',
  '{count} seconds': '{count} segundos',
  'Clear queue and close': 'Vaciar la cola y cerrar',
  '{count} tracks cannot be stored in a playlist and will be left out.':
    '{count} pistas no se pueden guardar en una lista y se omitirán.',
  'My mix': 'Mi mezcla',
  'Save': 'Guardar',
  'Name': 'Nombre',

  // bar, expanded view, queue
  'Player': 'Reproductor',
  'Open the player': 'Abrir el reproductor',
  'Expand': 'Expandir',
  'Now playing': 'Reproduciendo',
  'Minimize the player': 'Minimizar el reproductor',
  'Queue': 'Cola',
  '{count} tracks in the queue': '{count} pistas en la cola',
  'Drag or Alt+↑/↓ to reorder': 'Arrastra o usa Alt+↑/↓ para reordenar',
  'Remove from the queue': 'Quitar de la cola',

  // viewers
  'Download': 'Descargar',
  '{index} of {count}': '{index} de {count}',
  'The image could not be loaded': 'No se pudo cargar la imagen',
  'Zoom in': 'Acercar',
  'Zoom out': 'Alejar',
  'Reset zoom': 'Restablecer el zoom',
  'This file is too large to preview.': 'El fichero es demasiado grande para previsualizarlo.',
  'The file could not be loaded': 'No se pudo cargar el fichero',
  'There is no preview for this kind of file.': 'No hay vista previa para este tipo de fichero.',
  'This video cannot be played in the browser.': 'Este vídeo no se puede reproducir en el navegador.',
  'Back 10 seconds': 'Retroceder 10 segundos',
  'Forward 10 seconds': 'Avanzar 10 segundos',
  'Playback speed': 'Velocidad de reproducción',
  'Picture in picture': 'Imagen en imagen',
  'Full screen': 'Pantalla completa',
  'Exit full screen': 'Salir de pantalla completa'
};

export default player;
