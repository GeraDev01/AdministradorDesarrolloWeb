// El unico trabajo de este service worker es RECIBIR AVISOS con la pestana cerrada.
//
// A proposito NO cachea nada. La plantilla de PWA de Blazor guarda todos los archivos de la
// aplicacion para que funcione sin conexion, y aqui eso seria un mal negocio: esta aplicacion no
// hace nada util sin la API —todo lo que ensena viene de la base—, asi que el modo sin conexion
// solo serviria para pintar un armazon vacio. Y a cambio traeria el problema conocido de servir
// codigo viejo despues de un despliegue, que es de los que cuesta dias diagnosticar.
//
// Sin listener de 'fetch', el navegador sirve todo desde la red como siempre.

// Tomar el control sin esperar a que se cierren las pestanas abiertas: si no, el primer aviso no
// llegaria hasta que la persona cerrara todo, que en la practica es nunca.
self.addEventListener('install', function (evento) {
    evento.waitUntil(self.skipWaiting());
});

self.addEventListener('activate', function (evento) {
    evento.waitUntil(self.clients.claim());
});

self.addEventListener('push', function (evento) {
    if (!evento.data) return;

    let datos;
    try {
        datos = evento.data.json();
    } catch {
        // Un aviso que no se entiende no se enseña: un cuadro con texto ilegible es peor que nada.
        return;
    }

    evento.waitUntil(
        self.registration.showNotification(datos.titulo || 'Administrador de Desarrollo', {
            body: datos.cuerpo || '',
            // La direccion viaja en los datos para poder abrirla al pulsar.
            data: { url: datos.url || '/' },
            // Agrupa por direccion: cinco avisos del mismo hilo se apilan en vez de tapar la
            // pantalla uno encima de otro.
            tag: datos.url || 'general',
            renotify: false
        })
    );
});

self.addEventListener('notificationclick', function (evento) {
    evento.notification.close();

    const destino = (evento.notification.data && evento.notification.data.url) || '/';

    // Si ya hay una pestana de la aplicacion abierta se REUTILIZA. Abrir una nueva cada vez acaba
    // con diez pestanas iguales, y ademas se perderia lo que la persona estuviera escribiendo.
    evento.waitUntil(
        self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(function (pestanas) {
            for (const pestana of pestanas) {
                if ('focus' in pestana) {
                    pestana.navigate(destino);
                    return pestana.focus();
                }
            }
            if (self.clients.openWindow) return self.clients.openWindow(destino);
        })
    );
});
