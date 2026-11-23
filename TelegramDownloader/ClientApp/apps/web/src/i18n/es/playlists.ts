// Strings of the playlists feature (owned by that feature)
const playlists: Record<string, string> = {
  'Playlist': 'Lista de reproducción',
  'Playlists': 'Listas de reproducción',
  'Tracks of this playlist.': 'Pistas de esta lista.',
  'Your audio playlists.': 'Tus listas de reproducción de audio.',

  // list page
  'New playlist': 'Nueva lista',
  'Filter playlists': 'Filtrar listas',
  'Sort by': 'Ordenar por',
  'Recent': 'Recientes',
  'Tracks': 'Pistas',
  '{count} tracks': '{count} pistas',
  'Play "{name}"': 'Reproducir «{name}»',
  'Playlist options': 'Opciones de la lista',
  'Shuffle': 'Aleatorio',
  'Add to queue': 'Añadir a la cola',
  'Play next': 'Reproducir a continuación',
  'Rename': 'Renombrar',
  'Download to the server': 'Descargar al servidor',
  'Delete': 'Eliminar',
  'Could not load the playlists': 'No se pudieron cargar las listas',
  'No playlists yet': 'Aún no hay listas',
  'No playlist matches the filter': 'Ninguna lista coincide con el filtro',
  'Create one, or add tracks from any channel with "Add to playlist".':
    'Crea una o añade pistas desde cualquier canal con «Añadir a una lista».',

  // detail page
  'Play all': 'Reproducir todo',
  'Edit name and description': 'Editar el nombre y la descripción',
  'Delete playlist': 'Eliminar la lista',
  'Filter tracks': 'Filtrar pistas',
  'Clear the filter to reorder': 'Quita el filtro para reordenar',
  'Track options': 'Opciones de la pista',
  'Local file': 'Fichero local',
  'Remove from the playlist': 'Quitar de la lista',
  'Removed from the playlist': 'Quitada de la lista',
  'Could not remove the track': 'No se pudo quitar la pista',
  'Could not reorder the playlist': 'No se pudo reordenar la lista',
  'This playlist is empty': 'Esta lista está vacía',
  'Add tracks from any channel or from the local files with "Add to playlist".':
    'Añade pistas desde cualquier canal o desde los ficheros locales con «Añadir a una lista».',
  'Playlist not found': 'Lista no encontrada',
  'Could not load the playlist': 'No se pudo cargar la lista',
  'Back to playlists': 'Volver a las listas',

  // actions and dialogs
  'Delete "{name}"?': '¿Eliminar «{name}»?',
  'The playlist is deleted; the files stay where they are.': 'Se elimina la lista; los ficheros siguen donde están.',
  'Playlist deleted': 'Lista eliminada',
  'Could not delete the playlist': 'No se pudo eliminar la lista',
  'Edit playlist': 'Editar la lista',
  'Description': 'Descripción',
  'Create': 'Crear',
  'Playlist created': 'Lista creada',
  'Playlist updated': 'Lista actualizada',
  'Could not create the playlist': 'No se pudo crear la lista',
  'Could not update the playlist': 'No se pudo actualizar la lista',
  'Playlist "{name}" created with {count} tracks': 'Lista «{name}» creada con {count} pistas',
  'Copies the {count} tracks into the local storage of the server.': 'Copia las {count} pistas al almacenamiento local del servidor.',
  'Destination folder': 'Carpeta de destino',
  'Relative to the local storage root.': 'Relativa a la raíz del almacenamiento local.',
  'Download started': 'Descarga iniciada',
  'Follow it in Transfers.': 'Síguela en Transferencias.',
  'Could not start the download': 'No se pudo iniciar la descarga',

  // add-to-playlist picker
  'New playlist name': 'Nombre de la nueva lista',
  '{count} tracks added to "{name}"': '{count} pistas añadidas a «{name}»',
  'Already in "{name}"': 'Ya está en «{name}»',
  '{count} tracks were already there': '{count} pistas ya estaban',
  'Could not add to the playlist': 'No se pudo añadir a la lista',
  'Already added': 'Ya añadida'
};

export default playlists;
