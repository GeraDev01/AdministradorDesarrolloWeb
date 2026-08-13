// Lo poco que necesita tocar el navegador directamente. Todo lo demás vive en C#.
window.adminweb = {

    // Descarga lo que sirva una ruta de la API conservando el nombre que mande el servidor.
    // Se usa un enlace temporal y no window.open porque así la petición lleva la cookie de sesión y
    // el navegador respeta la cabecera Content-Disposition en lugar de abrir una pestaña en blanco.
    descargar: function (ruta) {
        const a = document.createElement('a');
        a.href = ruta;
        a.download = '';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
    },

    // rel=noopener es obligatorio: sin él, la página abierta puede manipular a la que la abrió.
    abrirEnOtraPestana: function (url) {
        window.open(url, '_blank', 'noopener,noreferrer');
    },

    // ── Atajo de la búsqueda global ──────────────────────────────────────────
    //
    // NO es Ctrl+K: ese lo reserva el navegador y no se le puede quitar de forma fiable, así que
    // funcionaría en unos y en otros no. Se usa «/» —la convención de GitHub y de casi todo lo que
    // vive dentro de un navegador— y Ctrl+Shift+K para quien venga del escritorio con el dedo hecho.
    //
    // Se registra UNA vez, aquí y no en el componente, para que no haya un escucha por cada vez que
    // Blazor lo vuelva a pintar.
    _atajoDeBusqueda: (function () {
        document.addEventListener('keydown', function (e) {
            const enCampo = /^(INPUT|TEXTAREA|SELECT)$/.test(e.target.tagName)
                         || e.target.isContentEditable;

            // Con «/» dentro de un campo se escribe una barra, que es lo que se espera.
            const conBarra = e.key === '/' && !enCampo && !e.ctrlKey && !e.altKey && !e.metaKey;
            const conCombo = e.ctrlKey && e.shiftKey && (e.key === 'K' || e.key === 'k');
            if (!conBarra && !conCombo) return;

            const caja = document.querySelector('.busqueda-caja');
            if (!caja) return;   // no es admin, o la barra todavía no está pintada

            e.preventDefault();
            caja.focus();
            caja.select();
        });
        return true;
    })(),

    // ── Las dos costuras del arrastrar y soltar ──────────────────────────────
    //
    // El gesto entero está escrito en C# con los eventos de Blazor; esto es lo único que desde allá
    // no se puede tocar, porque el objeto dataTransfer del evento no viaja de vuelta al navegador.
    //
    // La PRIMERA es obligatoria y no es una mejora: Firefox no inicia un arrastre si el manejador de
    // dragstart no deja algo escrito en dataTransfer. Sin esta línea, arrastrar funciona en Chrome y
    // en Edge y en Firefox no pasa absolutamente nada — el fallo más caro de diagnosticar, porque no
    // hay error en ninguna consola. Se escribe un tipo propio y NO texto plano: con texto plano,
    // soltar sin querer a una persona sobre cualquier caja de escritura de la página pegaría ahí la
    // etiqueta.
    //
    // La SEGUNDA es el cursor. Sin decir nada, el navegador dibuja el de COPIAR —un signo de más—
    // sobre los destinos que aceptan, y aquí no se copia a nadie: se le cambia de equipo. Se lee del
    // marcado (data-soltar), que es el mismo dato que decide si la caja acepta la soltada, así que
    // el cursor no puede contradecir a lo que va a ocurrir.
    //
    // Los dos escuchas se registran UNA vez sobre el documento, como el atajo de la búsqueda: uno
    // por caja arrastrable serían decenas, y habría que quitarlos cada vez que Blazor repinta. Y los
    // dos miran solo lo de dentro del organigrama: escuchando el documento entero, cualquier
    // arrastre que traiga un componente de Radzen —reordenar columnas de una rejilla, mover un
    // cuadro— se llevaría de rebote este tipo de dato y este efecto, que no son suyos.
    _arrastre: (function () {
        document.addEventListener('dragstart', function (e) {
            const origen = e.target && e.target.closest && e.target.closest('.organigrama [draggable="true"]');
            if (!origen || !e.dataTransfer) return;
            try {
                // ESTE setData ES LO QUE HACE QUE FIREFOX ARRANQUE EL ARRASTRE. Sin llamarlo, allí no
                // pasa nada al arrastrar: no se dispara el gesto, no hay error en consola y en Chrome
                // funciona perfectamente — o sea, el fallo más caro de diagnosticar que hay aquí.
                //
                // AVISO HONESTO: está razonado pero NO ejecutado en Firefox. Se probó el arrastre
                // entero con un ratón de verdad, pero sobre Chromium, que es lo único instalado y
                // también lo que usa el equipo hoy (la bitácora dice Edge, y ni un solo Firefox). Si
                // algún día alguien lo abre ahí y arrastrar no hace nada, éste es el sitio a mirar,
                // no el C#.
                e.dataTransfer.setData('application/x-adminweb', origen.getAttribute('data-arrastre') || '1');
                e.dataTransfer.effectAllowed = 'move';
            } catch (err) { /* el arrastre sigue; solo se queda sin la pista del tipo */ }
        });

        document.addEventListener('dragover', function (e) {
            if (!e.dataTransfer || !e.target || !e.target.closest) return;
            const destino = e.target.closest('.organigrama [data-soltar]');
            if (destino && destino.getAttribute('data-soltar') === 'si') e.dataTransfer.dropEffect = 'move';
        });

        return true;
    })(),

    // ── Tema claro / oscuro ──────────────────────────────────────────────────
    //
    // Quien APLICA el tema al arrancar es el guion de index.html, que corre antes de pintar nada.
    // Esto es solo lo que necesita el interruptor una vez que la aplicación ya está viva.

    // Devuelve 'claro', 'oscuro' o null (null = seguir al sistema).
    temaGuardado: function () {
        try { return localStorage.getItem('adminweb-tema'); } catch (e) { return null; }
    },

    // Con null se BORRA la preferencia en vez de guardar una cadena vacía: es lo que hace que el
    // atributo desaparezca del <html> y vuelva a mandar prefers-color-scheme del sistema.
    aplicarTema: function (tema) {
        try {
            if (tema === 'claro' || tema === 'oscuro') {
                localStorage.setItem('adminweb-tema', tema);
                document.documentElement.dataset.tema = tema;
            } else {
                localStorage.removeItem('adminweb-tema');
                delete document.documentElement.dataset.tema;
            }
        } catch (e) {
            // Sin almacenamiento (modo privado) el cambio vale para esta pestaña y ya.
            if (tema === 'claro' || tema === 'oscuro') document.documentElement.dataset.tema = tema;
            else delete document.documentElement.dataset.tema;
        }
    },

    copiar: async function (texto) {
        try {
            await navigator.clipboard.writeText(texto);
            return true;
        } catch {
            // Falla si no hay HTTPS o el navegador exige un gesto del usuario. Devolver false deja
            // que la interfaz lo diga en vez de fingir que se copió.
            return false;
        }
    },

    // ── Recorridos guiados ───────────────────────────────────────────────────
    //
    // El envoltorio de Driver.js, y nada más. Los GUIONES no están aquí: viven en C#
    // (Recorridos/), que es lo que permite que una prueba los lea y compruebe que cada paso
    // apunta a un control que sigue existiendo en el marcado. Escritos en JavaScript no habría
    // forma de comprobarlos, y un recorrido por pantalla sin comprobar envejece mal.
    //
    // Esta parte solo sabe tres cosas: buscar la marca en el documento, saltarse los pasos cuyo
    // control no esté, y recordar en el navegador que un recorrido ya se vio.
    recorridos: {

        // EL ATRIBUTO con el que se anclan los pasos. El mismo literal está en C#
        // (Paso.Atributo) y hay una prueba que comprueba que los dos dicen lo mismo: si se
        // cambiara solo aquí, los recorridos dejarían de encontrar nada y el síntoma sería que
        // todos los pasos «se saltan» — o sea, ningún error, solo recorridos vacíos.
        ATRIBUTO: 'data-recorrido',

        _clave: 'adminweb-recorrido-visto:',
        _activo: null,
        _sinContar: false,

        _selector: function (marca) {
            // La marca es siempre [a-z0-9-] —hay una prueba que lo exige—, así que el escape es
            // un cinturón de más; cuesta nada y evita que una marca rara rompa el selector entero.
            return '[' + window.adminweb.recorridos.ATRIBUTO + '="' +
                   String(marca).replace(/["\\]/g, '\\$&') + '"]';
        },

        // «Está y se ve». querySelector encuentra también lo que está oculto —una pestaña que no
        // toca, un panel plegado, una columna escondida—, y señalar algo invisible deja el globo
        // apuntando a un rectángulo de cero por cero en una esquina.
        //
        // Se mira el tamaño y no offsetParent porque lo que está en posición fija no tiene padre
        // de posición y aun así se ve perfectamente: la barra superior, sin ir más lejos.
        _visible: function (el) {
            return !!(el && (el.offsetWidth || el.offsetHeight || el.getClientRects().length));
        },

        /**
         * Lanza un recorrido. Devuelve cuántos pasos se enseñaron de verdad.
         *
         * @param {string} id     con qué nombre se recuerda que ya se vio (la ruta de la pantalla)
         * @param {Array}  pasos  [{ marca, titulo, texto, lado }], ya en el orden bueno
         */
        iniciar: function (id, pasos) {
            const R = window.adminweb.recorridos;

            R.cerrar();

            if (!window.driver || !window.driver.js || typeof window.driver.js.driver !== 'function') {
                // index.html carga driver.js ANTES que este archivo. Si falta, es que alguien tocó
                // ese orden; se dice en la consola en vez de reventar la pantalla.
                console.warn('Recorridos: driver.js no está cargado; no hay nada que enseñar.');
                return 0;
            }

            const lados = { arriba: 'top', abajo: 'bottom', izquierda: 'left', derecha: 'right' };
            const guion = [];

            for (const p of (pasos || [])) {
                const paso = { popover: { title: p.titulo || '', description: p.texto || '' } };

                if (p.marca) {
                    // AQUÍ ESTÁ LA TOLERANCIA A FALLOS. Hay controles que solo existen para ciertos
                    // roles, o cuando hay datos, o con un panel abierto. Un paso cuyo control no
                    // esté se SALTA y el recorrido continúa: quedarse a medias por eso sería
                    // castigar a quien pide ayuda por una condición que no eligió.
                    //
                    // Lo que no se hace es callarlo del todo en el desarrollo: si la marca no está
                    // en NINGUNA parte del documento, la prueba de la suite ya debería estar roja, y
                    // el aviso de consola es lo que conecta ese fallo con lo que se ve.
                    const selector = R._selector(p.marca);
                    const el = document.querySelector(selector);
                    if (!R._visible(el)) continue;

                    // Se pasa el SELECTOR y no el nodo: Blazor vuelve a pintar cuando le parece, y
                    // un nodo capturado ahora puede estar ya sustituido cuando el recorrido llegue
                    // a ese paso. Con el selector, Driver.js lo busca en el momento de señalarlo.
                    paso.element = selector;

                    const lado = lados[p.lado];
                    if (lado) paso.popover.side = lado;
                }
                // Sin marca es la PORTADA: Driver.js la enseña centrada, sin señalar nada. Nunca se
                // salta, y por eso un recorrido que empieza con una jamás sale vacío.

                guion.push(paso);
            }

            if (guion.length === 0) return 0;

            const conductor = window.driver.js.driver({
                steps: guion,
                animate: true,
                // Cerrar con Escape y pulsando fuera. Los dos vienen de serie con esto puesto, y son
                // la mitad de que la gente se atreva a lanzar el recorrido: se prueba lo que se sabe
                // abandonar.
                allowClose: true,
                allowKeyboardControl: true,
                overlayOpacity: 0.6,
                stagePadding: 6,
                stageRadius: 6,
                smoothScroll: true,
                showProgress: true,
                popoverClass: 'recorrido-adminweb',
                progressText: '{{current}} de {{total}}',
                nextBtnText: 'Siguiente',
                prevBtnText: 'Anterior',
                doneBtnText: 'Terminar',
                onDestroyed: function () {
                    R._activo = null;
                    // Se apunta como visto tanto al terminarlo como al cerrarlo a medias: las dos
                    // cosas significan «ya sé que esto existe». Lo único que decide es si el botón
                    // enseña el puntito de «sin estrenar»; no hay ningún recorrido que salte solo.
                    if (!R._sinContar) R.marcarVisto(id);
                    R._sinContar = false;
                }
            });

            R._activo = conductor;
            conductor.drive();
            return guion.length;
        },

        /**
         * Cierra el recorrido en curso. Se llama al cambiar de pantalla.
         *
         * Cerrar así NO lo cuenta como visto: quien navega a otra parte no lo ha visto, y darlo por
         * visto escondería el aviso de que hay uno sin estrenar.
         */
        cerrar: function () {
            const R = window.adminweb.recorridos;
            if (!R._activo) return;

            R._sinContar = true;
            try {
                R._activo.destroy();
            } catch (e) {
                // Si el cierre ordenado falla, lo que NO puede quedarse puesto es la clase que
                // Driver.js pone en el <body>: mientras está, su hoja deja toda la aplicación sin
                // recibir clics. El fallo sería una aplicación aparentemente colgada, sin más
                // síntoma. Esto es el último recurso para que eso no ocurra nunca.
                document.body.classList.remove('driver-active', 'driver-fade', 'driver-simple');
                document.querySelectorAll('.driver-overlay, .driver-popover').forEach(function (n) {
                    n.remove();
                });
                console.warn('Recorridos: el cierre ordenado falló; se limpió a mano.', e);
            }
            R._activo = null;
            R._sinContar = false;
        },

        enMarcha: function () {
            return !!window.adminweb.recorridos._activo;
        },

        visto: function (id) {
            try {
                return localStorage.getItem(window.adminweb.recorridos._clave + id) === '1';
            } catch (e) {
                // Sin almacenamiento (modo privado): se da por visto para no insistir con el punto.
                return true;
            }
        },

        marcarVisto: function (id) {
            try {
                localStorage.setItem(window.adminweb.recorridos._clave + id, '1');
            } catch (e) { /* modo privado: vale para esta pestaña y ya */ }
        }
    },

    // Pegar una captura dentro de un recuadro.
    //
    // Es lo único que C# no puede hacer solo: el evento «paste» de Blazor no trae los archivos, solo
    // el hecho de que se pegó. Los bytes están en event.clipboardData y hay que leerlos aquí.
    //
    // No se usa navigator.clipboard.read() —que sería más directo— porque pide un permiso aparte y
    // varios navegadores lo niegan de plano; el evento, en cambio, funciona en todos y no pide nada,
    // porque el usuario ya expresó su intención al pulsar Ctrl+V.
    _pegados: new WeakMap(),

    escucharPegado: function (elemento, referenciaDotNet) {
        if (!elemento || this._pegados.has(elemento)) return;

        const manejador = async function (evento) {
            const elementos = (evento.clipboardData || window.clipboardData)?.items;
            if (!elementos) return;

            const archivos = [];
            for (const it of elementos) {
                if (it.kind === 'file' && it.type.startsWith('image/')) {
                    const f = it.getAsFile();
                    if (f) archivos.push(f);
                }
            }
            if (archivos.length === 0) return;   // se pegó texto: que siga su curso normal

            // Solo se corta el pegado cuando de verdad hay imágenes; si no, pegar texto en una caja
            // dentro del recuadro dejaría de funcionar.
            evento.preventDefault();

            for (const archivo of archivos) {
                try {
                    const base64 = await window.adminweb._aBase64(archivo);
                    // Una captura pegada no trae nombre: el navegador la llama «image.png».
                    const nombre = archivo.name && archivo.name !== 'image.png'
                        ? archivo.name
                        : 'captura_' + new Date().toISOString().replace(/[:.]/g, '-') + '.png';
                    await referenciaDotNet.invokeMethodAsync('RecibirPegado', nombre, archivo.type, base64);
                } catch (e) {
                    console.error('No se pudo leer la imagen pegada', e);
                }
            }
        };

        this._pegados.set(elemento, manejador);
        elemento.addEventListener('paste', manejador);
    },

    dejarDeEscucharPegado: function (elemento) {
        if (!elemento) return;
        const manejador = this._pegados.get(elemento);
        if (manejador) {
            elemento.removeEventListener('paste', manejador);
            this._pegados.delete(elemento);
        }
    },

    // Genera la miniatura de una imagen y devuelve tambien sus medidas reales.
    //
    // Se hace AQUI y no en el servidor por dos razones. La primera es que el navegador ya tiene la
    // imagen decodificada: reescalarla le cuesta unos milisegundos, mientras que el servidor
    // tendria que decodificar cada subida de cada persona. La segunda es la que decide: hacerlo en
    // el servidor obligaria a meter una libreria de imagenes, y las que sirven o no son libres para
    // uso comercial o traen binarios nativos por plataforma. Aqui no entra ninguna dependencia.
    //
    // El servidor NO se fia de lo que salga de aqui: comprueba que la miniatura sea de verdad una
    // imagen y que quepa. Esto es comodidad y ahorro, no una barrera.
    miniatura: function (base64Original, tipo, maxLado) {
        return new Promise(function (resolver) {
            const img = new Image();

            img.onload = function () {
                const lado = Math.max(img.width, img.height);
                const escala = lado > maxLado ? maxLado / lado : 1;

                const lienzo = document.createElement('canvas');
                lienzo.width = Math.max(1, Math.round(img.width * escala));
                lienzo.height = Math.max(1, Math.round(img.height * escala));

                const ctx = lienzo.getContext('2d');
                ctx.drawImage(img, 0, 0, lienzo.width, lienzo.height);

                // JPEG y no PNG: una captura de pantalla reescalada en PNG puede pesar mas que el
                // propio original reducido, y lo que se busca aqui son unos pocos KB. La calidad no
                // importa porque nadie mira la miniatura de cerca — para eso se abre el original.
                const datos = lienzo.toDataURL('image/jpeg', 0.7);

                resolver({
                    Base64: datos.split(',')[1] || '',
                    Ancho: img.width,
                    Alto: img.height
                });
            };

            // Si el navegador no sabe decodificarla, se sigue sin miniatura: el servidor guardara el
            // original como miniatura, que es lo que hacia antes. Peor, pero no roto.
            img.onerror = function () { resolver(null); };

            img.src = 'data:' + tipo + ';base64,' + base64Original;
        });
    },

    // ── Avisos con la pestana cerrada ───────────────────────────────────────────
    //
    // Sustituyen a los globos de la bandeja del sistema, que era lo unico que la web no podia hacer.
    // Hacen falta tres cosas del navegador: registrar el service worker, pedir permiso a la persona
    // (solo el navegador puede pedirlo, y solo tras un gesto suyo) y suscribirse al servicio de
    // entrega. Lo que sale de aqui es lo que el servidor guarda para poder entregarle avisos.
    push: {
        soportado: function () {
            return 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
        },

        // 'default' = todavia no se ha preguntado, 'granted' = concedido, 'denied' = denegado.
        // El denegado importa: el navegador NO vuelve a preguntar, hay que quitarlo a mano desde su
        // configuracion, y la pantalla tiene que decirlo en vez de ofrecer un boton que no hace nada.
        permiso: function () {
            return window.adminweb.push.soportado() ? Notification.permission : 'no-soportado';
        },

        // Devuelve la suscripcion actual, o null. Se consulta al entrar para saber si este navegador
        // ya dijo que si — la persona no deberia tener que volver a decidirlo en cada visita.
        actual: async function () {
            if (!window.adminweb.push.soportado()) return null;
            try {
                const registro = await navigator.serviceWorker.ready;
                const suscripcion = await registro.pushManager.getSubscription();
                return suscripcion ? window.adminweb.push._describir(suscripcion) : null;
            } catch {
                return null;
            }
        },

        // Registra el service worker, pide permiso y se suscribe. Devuelve la suscripcion o un
        // objeto con el motivo, para que la pantalla pueda explicarlo en vez de fallar en silencio.
        suscribir: async function (llavePublica) {
            if (!window.adminweb.push.soportado()) return { Error: 'no-soportado' };

            try {
                const registro = await navigator.serviceWorker.register('/service-worker.js');
                await navigator.serviceWorker.ready;

                const permiso = await Notification.requestPermission();
                if (permiso !== 'granted') return { Error: permiso };

                let suscripcion = await registro.pushManager.getSubscription();
                if (!suscripcion) {
                    suscripcion = await registro.pushManager.subscribe({
                        // Obligatorio en todos los navegadores actuales: no se admiten avisos
                        // silenciosos, cada push tiene que enseñar algo. Es una decision de ellos.
                        userVisibleOnly: true,
                        applicationServerKey: window.adminweb.push._aBytes(llavePublica)
                    });
                }
                return window.adminweb.push._describir(suscripcion);
            } catch (e) {
                console.error('No se pudo activar el aviso push', e);
                return { Error: 'fallo' };
            }
        },

        cancelar: async function () {
            if (!window.adminweb.push.soportado()) return null;
            try {
                const registro = await navigator.serviceWorker.ready;
                const suscripcion = await registro.pushManager.getSubscription();
                if (!suscripcion) return null;

                const endpoint = suscripcion.endpoint;
                await suscripcion.unsubscribe();
                return endpoint;
            } catch {
                return null;
            }
        },

        _describir: function (suscripcion) {
            const bruta = suscripcion.toJSON();
            return {
                Endpoint: bruta.endpoint,
                P256dh: bruta.keys.p256dh,
                Auth: bruta.keys.auth,
                // Lo justo para reconocer el equipo al retirar el permiso. NO se manda el agente
                // completo: no hace falta y es huella de seguimiento.
                Descripcion: navigator.platform || 'navegador'
            };
        },

        // La llave VAPID viaja en base64url y el navegador la exige como bytes.
        _aBytes: function (base64url) {
            const relleno = '='.repeat((4 - (base64url.length % 4)) % 4);
            const base64 = (base64url + relleno).replace(/-/g, '+').replace(/_/g, '/');
            const crudo = window.atob(base64);
            const bytes = new Uint8Array(crudo.length);
            for (let i = 0; i < crudo.length; i++) bytes[i] = crudo.charCodeAt(i);
            return bytes;
        }
    },

    // ── Firma manuscrita ────────────────────────────────────────────────────────
    //
    // En el escritorio era un control que dibujaba con GDI+ y solo respondia al raton. Aqui se usan
    // eventos de PUNTERO, que unifican raton, dedo y lapiz: quien firme desde una tableta lo hara
    // con el dedo, que es como se firma de verdad. Es de las pocas cosas que la web hace mejor.
    //
    // El trazo se dibuja en el lienzo mientras se mueve; C# no se entera hasta que se pide el PNG.
    // Mandar cada punto por interoperabilidad haria la firma inservible: llegaria a trompicones.
    firma: {
        iniciar: function (lienzo) {
            if (!lienzo || lienzo._firmaLista) return;

            const ctx = lienzo.getContext('2d');

            // El lienzo se dimensiona a los pixeles REALES de la pantalla. Sin esto, en una pantalla
            // de alta densidad el trazo sale borroso y con el grosor equivocado — y esa imagen acaba
            // impresa en un documento que alguien archiva.
            const escala = window.devicePixelRatio || 1;
            const caja = lienzo.getBoundingClientRect();
            lienzo.width = Math.round(caja.width * escala);
            lienzo.height = Math.round(caja.height * escala);
            ctx.scale(escala, escala);

            ctx.lineWidth = 2;
            ctx.lineCap = 'round';
            ctx.lineJoin = 'round';

            // LA TINTA, Y SE QUEDA FIJA A PROPOSITO: no la ates al tema.
            //
            // Cuando alguien reporta «en modo oscuro no se ve la firma», esta linea parece la
            // culpable —grafito sobre fondo oscuro— y es justo la que no hay que tocar. El PNG que
            // sale de aqui es transparente y se pega en un documento que alguien imprime y archiva
            // sobre papel blanco de verdad: si la tinta se aclarara con el tema, el documento
            // archivado saldria en blanco sobre blanco, y eso no se descubre hasta tener la hoja
            // firmada en la mano. Lo que se adapta es el PAPEL, no la tinta — el fondo del lienzo lo
            // pone el CSS de FirmaEnLienzo y vale igual en los dos modos.
            //
            // Y ese fondo se queda en el CSS: el background de un <canvas> NO forma parte de su mapa
            // de bits, asi que toDataURL() y getImageData() siguen viendo solo el trazo. Pintar aqui
            // el fondo con un fillRect es el atajo que rompe la transparencia del PNG. No se hace.
            ctx.strokeStyle = '#1f2937';

            let trazando = false;
            lienzo._firmaVacia = true;

            const punto = function (e) {
                const c = lienzo.getBoundingClientRect();
                return { x: e.clientX - c.left, y: e.clientY - c.top };
            };

            lienzo.addEventListener('pointerdown', function (e) {
                trazando = true;
                lienzo._firmaVacia = false;
                // Capturar el puntero mantiene el trazo aunque el dedo se salga del recuadro: sin
                // esto, salirse por un lado deja la linea colgada a medias.
                lienzo.setPointerCapture(e.pointerId);
                const p = punto(e);
                ctx.beginPath();
                ctx.moveTo(p.x, p.y);
                e.preventDefault();
            });

            lienzo.addEventListener('pointermove', function (e) {
                if (!trazando) return;
                const p = punto(e);
                ctx.lineTo(p.x, p.y);
                ctx.stroke();
                e.preventDefault();
            });

            const soltar = function (e) {
                if (!trazando) return;
                trazando = false;
                try { lienzo.releasePointerCapture(e.pointerId); } catch { }
            };
            lienzo.addEventListener('pointerup', soltar);
            lienzo.addEventListener('pointercancel', soltar);
            lienzo.addEventListener('pointerleave', soltar);

            lienzo._firmaLista = true;
        },

        limpiar: function (lienzo) {
            if (!lienzo) return;
            const ctx = lienzo.getContext('2d');
            // clearRect y NO un fillRect del color del papel: borrar rellenando dejaria el bitmap
            // opaco y la siguiente firma se guardaria con un rectangulo de fondo encima del
            // documento. Lo que se ve de fondo mientras se firma lo pone el CSS. Ver la nota de la
            // tinta en iniciar().
            ctx.clearRect(0, 0, lienzo.width, lienzo.height);
            lienzo._firmaVacia = true;
        },

        vacia: function (lienzo) {
            return !lienzo || lienzo._firmaVacia !== false;
        },

        // El PNG con FONDO TRANSPARENTE y recortado a lo dibujado. Las dos cosas importan: la firma
        // se pega dentro de un documento, y un rectangulo blanco encima del papel se nota. El recorte
        // ademas evita que una firma pequena en un lienzo grande salga diminuta al escalarla.
        png: function (lienzo) {
            if (!lienzo || lienzo._firmaVacia !== false) return null;

            const ctx = lienzo.getContext('2d');
            const datos = ctx.getImageData(0, 0, lienzo.width, lienzo.height);
            const p = datos.data;

            let x0 = lienzo.width, y0 = lienzo.height, x1 = -1, y1 = -1;
            for (let y = 0; y < lienzo.height; y++) {
                for (let x = 0; x < lienzo.width; x++) {
                    // El canal alfa: lo que no se dibujo es transparente.
                    if (p[(y * lienzo.width + x) * 4 + 3] > 0) {
                        if (x < x0) x0 = x;
                        if (y < y0) y0 = y;
                        if (x > x1) x1 = x;
                        if (y > y1) y1 = y;
                    }
                }
            }
            if (x1 < 0) return null;

            // Un margen para que el trazo no quede pegado al borde del recorte.
            const margen = 6;
            x0 = Math.max(0, x0 - margen); y0 = Math.max(0, y0 - margen);
            x1 = Math.min(lienzo.width - 1, x1 + margen); y1 = Math.min(lienzo.height - 1, y1 + margen);

            const ancho = x1 - x0 + 1, alto = y1 - y0 + 1;
            const recorte = document.createElement('canvas');
            recorte.width = ancho;
            recorte.height = alto;
            recorte.getContext('2d').drawImage(lienzo, x0, y0, ancho, alto, 0, 0, ancho, alto);

            return {
                Base64: recorte.toDataURL('image/png').split(',')[1] || '',
                Ancho: ancho,
                Alto: alto
            };
        },

        // Importa una firma ESCANEADA (o fotografiada) y la deja como si se hubiera trazado aqui:
        // fondo transparente y recortada al trazo.
        //
        // Se hace en el navegador y no en el servidor a proposito. La logica es la misma que traia
        // SignatureImaging con GDI+, pero GDI+ no corre en Linux, y traerla al servidor obligaria a
        // meter un paquete de imagen (ImageSharp cambio a licencia comercial; SkiaSharp es MIT pero
        // son ~20 MB de binarios nativos) para hacer sobre pixeles lo que el <canvas> ya hace de
        // serie. El servidor sigue validando lo que le llega: tamano, que sea PNG por sus bytes y
        // que las dimensiones sean razonables — nunca se fia del recorte que le manden.
        //
        // El umbral es 240 y no 255 porque un escaneo no tiene blancos puros: el papel sale en
        // 245-250 y con 255 no se volveria transparente ni un pixel. Se mira el canal MENOR de los
        // tres para no comerse un trazo de tinta azul claro, que en rojo puede pasar de 240.
        limpiarEscaneada: function (archivo, umbral) {
            umbral = umbral || 240;

            return new Promise(function (resolver, rechazar) {
                const url = URL.createObjectURL(archivo);
                const img = new Image();

                img.onload = function () {
                    URL.revokeObjectURL(url);
                    try {
                        // Un escaneo a 600 ppp puede venir enorme y no aporta nada: la firma se
                        // imprime en unos 190x60 puntos. Se reduce antes de recorrer los pixeles,
                        // que ademas es lo que evita recorrer 30 millones de ellos.
                        const tope = 1200;
                        const escala = Math.min(1, tope / Math.max(img.width, img.height));
                        const ancho = Math.max(1, Math.round(img.width * escala));
                        const alto = Math.max(1, Math.round(img.height * escala));

                        const lienzo = document.createElement('canvas');
                        lienzo.width = ancho;
                        lienzo.height = alto;
                        const ctx = lienzo.getContext('2d', { willReadFrequently: true });
                        ctx.drawImage(img, 0, 0, ancho, alto);

                        const datos = ctx.getImageData(0, 0, ancho, alto);
                        const p = datos.data;
                        for (let i = 0; i < p.length; i += 4) {
                            if (Math.min(p[i], p[i + 1], p[i + 2]) >= umbral) p[i + 3] = 0;
                        }
                        ctx.putImageData(datos, 0, 0);

                        // Se marca como «no vacia» para que png() acepte recortarla: esa bandera la
                        // pone normalmente el trazo del raton, y aqui no hubo ninguno.
                        lienzo._firmaVacia = false;
                        const png = window.adminweb.firma.png(lienzo);

                        if (!png) {
                            rechazar(new Error(
                                'La imagen se quedo en blanco al quitarle el fondo. Suele pasar con una ' +
                                'foto muy clara: prueba con un escaneo, o con la foto mejor iluminada.'));
                            return;
                        }
                        resolver(png);
                    } catch (e) {
                        rechazar(e);
                    }
                };

                img.onerror = function () {
                    URL.revokeObjectURL(url);
                    rechazar(new Error('Ese archivo no es una imagen que el navegador pueda abrir.'));
                };

                img.src = url;
            });
        },

        // Lo mismo, pero recibiendo los bytes en base64 desde C#.
        //
        // Blazor lee el archivo del <InputFile> del lado de C#, asi que el objeto File del navegador
        // ya no esta disponible cuando toca limpiarlo. Se reconstruye un Blob con los bytes, que es
        // lo que Image sabe abrir. El rodeo cuesta una copia en memoria de unos pocos megabytes y
        // evita tener DOS caminos de subida —uno por JavaScript y otro por Blazor— con dos sitios
        // distintos donde validar el tamano.
        limpiarEscaneadaDesdeBytes: function (base64, tipo) {
            const binario = atob(base64);
            const bytes = new Uint8Array(binario.length);
            for (let i = 0; i < binario.length; i++) bytes[i] = binario.charCodeAt(i);

            return window.adminweb.firma.limpiarEscaneada(
                new Blob([bytes], { type: tipo || 'image/png' }));
        }
    },

    _aBase64: function (archivo) {
        return new Promise(function (resolver, rechazar) {
            const lector = new FileReader();
            // readAsDataURL devuelve «data:image/png;base64,AAAA…»; a C# solo le interesa lo de después.
            lector.onload = function () { resolver(String(lector.result).split(',')[1] || ''); };
            lector.onerror = function () { rechazar(lector.error); };
            lector.readAsDataURL(archivo);
        });
    },

    // ── El organigrama, puesto donde se empieza a leer ───────────────────────
    //
    // Un organigrama clásico crece A LO ANCHO, y la caja de arriba va centrada sobre todas las
    // demás. Con los veinte equipos raíz de esta casa el lienzo mide varias pantallas y esa caja
    // cae por la mitad: al abrir la pestaña, el desplazamiento empieza en cero y lo que se ve es
    // el borde izquierdo del dibujo —los primeros equipos y, encima de ellos, una BANDA VACÍA
    // donde debería estar la cabeza—. Parece que la pantalla no cargó.
    //
    // Esto lo coloca mirando a la caja de arriba, que es por donde se lee un organigrama. No lo
    // puede hacer la hoja de estilo: el desplazamiento de un elemento no es una propiedad de CSS.
    //
    // Se llama después de pintar, y se aguanta que no haya nada que centrar: la pestaña puede
    // estar cerrada, o los datos todavía en camino.
    centrarOrganigrama: function (selector) {
        const lienzo = document.querySelector(selector || '.organigrama');
        if (!lienzo) return;

        const sobra = lienzo.scrollWidth - lienzo.clientWidth;
        if (sobra <= 0) return;   // cabe entero: no hay nada que colocar

        // La caja de arriba es la primera del recorrido, que es como la manda el servidor.
        const cabeza = lienzo.querySelector('.organigrama-caja');
        if (!cabeza) return;

        const centroDeLaCabeza = cabeza.offsetLeft + cabeza.offsetWidth / 2;
        const destino = centroDeLaCabeza - lienzo.clientWidth / 2;

        // «instant» y no suave: esto ocurre al abrir la pestaña, y una animación de tres mil
        // píxeles al entrar se lee como un fallo, no como una ayuda.
        lienzo.scrollTo({ left: Math.max(0, Math.min(destino, sobra)), behavior: 'instant' });
    }
};
