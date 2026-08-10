# Las fuentes

Viajan **en el repositorio** y no se piden a un CDN. Dos razones, y ninguna es estética: la
aplicación tiene que verse igual en una red corporativa que corte `fonts.googleapis.com`, y una
fuente que tarda en llegar es un salto de composición en cada carga.

| Archivo | Qué es | Licencia |
|---|---|---|
| `PlexSans-latin.woff2`, `PlexSans-latin-ext.woff2` | IBM Plex Sans, variable 400–700. Todo el texto | OFL 1.1 |
| `PlexMono-latin.woff2`, `PlexMono-latin-ext.woff2` | IBM Plex Mono 400. Identificadores y correlaciones | OFL 1.1 |
| `MaterialSymbolsSharp.woff2` | Los iconos. **Recortado**, ver abajo | Apache 2.0 |
| `iconos.txt` | La lista exacta de iconos que lleva el recorte | — |

Las tres son libres, también para uso comercial. No hace falta pagar ni atribuir en la interfaz.

---

## ⚠ El archivo de iconos está recortado

`MaterialSymbolsSharp.woff2` **no** es la fuente completa: lleva solo los iconos de `iconos.txt`.

|  | Tamaño |
|---|---|
| Material Symbols Sharp completa | 3,5 MB |
| La que trae Radzen (Outlined, completa) | 1,0 MB |
| **Ésta, recortada** | **0,15 MB** |

Es la diferencia entre que el menú aparezca al instante o después de la fuente, en la primera carga
de cada persona.

**Qué pasa si usas un icono que no está**: se ve su nombre escrito tal cual, en letras, dentro del
botón — `delete` en vez del bote de basura. Falla a la vista y no en silencio, que es lo que se
buscaba, pero hay que regenerar el archivo.

### Cómo regenerarlo

Al añadir o cambiar un icono, añade su nombre a `iconos.txt` y vuelve a pedir la fuente. La lista
tiene que ir **ordenada alfabéticamente y sin repetidos**: es un requisito de Google Fonts, y si no
se cumple la respuesta viene vacía sin decir por qué.

```bash
cd src/Web/AdminWeb.Client/wwwroot/fuentes
UA="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36"

sort -u -o iconos.txt iconos.txt
N=$(tr '\n' ',' < iconos.txt | sed 's/,$//')

URL=$(curl -s -A "$UA" \
  "https://fonts.googleapis.com/css2?family=Material+Symbols+Sharp:opsz,wght,FILL,GRAD@20..48,100..700,0..1,-50..200&icon_names=$N" \
  | grep -oP 'https://[^)]+' | head -1)

curl -s -A "$UA" -o MaterialSymbolsSharp.woff2 "$URL"
```

El `-A` con un navegador moderno **no es opcional**: sin él Google devuelve un `.ttf` de otra época,
que pesa el triple y no admite ejes variables.

### Qué NO quitar de la lista

`iconos.txt` incluye los que usa **Radzen por dentro** y que no aparecen en ningún `Icon="…"` del
código: las flechas del paginador, el aspa de cerrar diálogos, los cheurones de los desplegables,
las flechas de ordenación de las rejillas, el calendario del selector de fechas. Se sacaron de su
CSS (`content:"…"`). Si desaparecen de aquí, los controles de Radzen se llenan de palabras.
