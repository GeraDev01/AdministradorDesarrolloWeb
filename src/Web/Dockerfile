# La réplica web, empaquetada.
#
# El contexto de construcción es la RAÍZ del repositorio, no esta carpeta: hace falta el nuget.config
# de arriba, que es el que asigna a su origen los paquetes que la configuración global de estos
# equipos desvía a un feed privado. Sin él la restauración falla con NU1100 en cualquier máquina que
# no tenga ese feed configurado — incluido el contenedor.
#
#   docker build -f src/Web/Dockerfile -t adminweb .

# ── Construir ───────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS construccion
WORKDIR /origen

# Primero SOLO los archivos de proyecto y la configuración de NuGet, y se restaura. Así la capa de
# restauración se reutiliza mientras no cambien las dependencias, que es lo que hace que una
# reconstrucción tras tocar código tarde segundos en vez de minutos.
COPY nuget.config ./
COPY src/Web/Directory.Build.props src/Web/
COPY src/Web/AdminWeb.Shared/AdminWeb.Shared.csproj                         src/Web/AdminWeb.Shared/
COPY src/Web/AdminWeb.Domain/AdminWeb.Domain.csproj                         src/Web/AdminWeb.Domain/
COPY src/Web/AdminWeb.Infrastructure/AdminWeb.Infrastructure.csproj         src/Web/AdminWeb.Infrastructure/
COPY src/Web/AdminWeb.Application/AdminWeb.Application.csproj               src/Web/AdminWeb.Application/
COPY src/Web/AdminWeb.Client/AdminWeb.Client.csproj                         src/Web/AdminWeb.Client/
COPY src/Web/AdminWeb.Api/AdminWeb.Api.csproj                               src/Web/AdminWeb.Api/
RUN dotnet restore src/Web/AdminWeb.Api/AdminWeb.Api.csproj

# Y ahora el código. Solo src/Web: la aplicación de escritorio no entra en la imagen —es net10.0
# -windows y arrastra WinForms— y copiarla solo serviría para invalidar esta capa cada vez que
# alguien la toque.
COPY src/Web/ src/Web/
RUN dotnet publish src/Web/AdminWeb.Api/AdminWeb.Api.csproj \
    -c Release -o /publicado --no-restore

# ── Ejecutar ────────────────────────────────────────────────────────────────────
#
# Imagen de RUNTIME, no de SDK: la de construcción pesa más de un giga y lleva compiladores que en
# un servidor solo son superficie de ataque.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /aplicacion

# curl, solo para el HEALTHCHECK. La imagen de runtime no trae NINGÚN cliente HTTP —ni curl ni
# wget—, así que un healthcheck que los invoque falla siempre con «no such file or directory» y el
# contenedor se queda «unhealthy» para siempre aunque la aplicación responda perfectamente. Eso, en
# un orquestador, significa que nunca se da por listo o que se reinicia en bucle.
#
# Se instala ANTES de cambiar de usuario porque apt necesita root, y se limpia la caché en la misma
# capa: dejarla añadiría decenas de megas a la imagen final.
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Sin privilegios. La imagen de ASP.NET ya trae el usuario «app»; que el proceso no sea root es lo
# que limita el daño si algún día se cuela algo por una subida de archivos.
USER app

COPY --from=construccion /publicado .

# Dentro del contenedor se sirve por HTTP en claro a propósito: quien pone el TLS es lo que hay
# delante (App Service, o el proxy). Terminar TLS aquí obligaría a meter certificados en la imagen.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# El healthcheck usa /api/health, que consulta la BASE y no solo el proceso: un contenedor que
# arrancó pero no puede migrar ni consultar no está sano, y decir que sí lo está es lo que hace que
# un despliegue roto pase el intercambio de ranuras.
HEALTHCHECK --interval=10s --timeout=5s --start-period=90s --retries=10 \
    CMD curl -fsS http://127.0.0.1:8080/api/health || exit 1

ENTRYPOINT ["dotnet", "AdminWeb.Api.dll"]
