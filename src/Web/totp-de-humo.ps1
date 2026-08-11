# El calculo del codigo de seis digitos del segundo factor, para las pruebas de humo.
#
# Lo usan humo.ps1 y humo-docker.ps1, que desde que el segundo factor es obligatorio no pueden
# recorrer la aplicacion sin teclear un codigo. Vive en un archivo aparte y no copiado en los dos
# porque es la clase de codigo que se corrige una vez y se olvida en la otra copia.
#
# ESTA ESCRITO A MANO Y NO LLAMA A LA APLICACION, y eso es deliberado: si el codigo saliera de la
# misma pieza que se esta probando, la prueba pasaria igual de bien con las dos equivocadas. Aqui se
# implementa el RFC 6238 desde cero —HMAC-SHA1 sobre el numero de ventana— y se comprueba contra el
# servidor, que es lo unico que da informacion.
#
# Escrito para Windows PowerShell 5.1.

function ConvertirDeBase32([string]$texto) {
    $alfabeto = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"

    # Cinco bits por caracter, empujados a un acumulador del que salen de ocho en ocho. Lo que no
    # pertenece al alfabeto (relleno, espacios, guiones) se ignora: la clave se puede pegar tal como
    # la ensena la pantalla, en grupos de cuatro.
    $bits = 0
    $acumulado = 0
    $bytes = New-Object System.Collections.Generic.List[byte]

    foreach ($caracter in $texto.ToUpperInvariant().ToCharArray()) {
        $indice = $alfabeto.IndexOf($caracter)
        if ($indice -lt 0) { continue }

        $acumulado = ($acumulado -shl 5) -bor $indice
        $bits += 5
        if ($bits -ge 8) {
            $bits -= 8
            $bytes.Add([byte](($acumulado -shr $bits) -band 0xFF))
        }
    }

    return $bytes.ToArray()
}

# El codigo de la ventana en curso, o el de N ventanas mas alla.
#
# El desplazamiento existe por la ANTIRREPETICION del servidor: un codigo aceptado no vuelve a valer,
# asi que si el guion acaba de dar de alta el segundo factor y quiere entrar acto seguido, tiene que
# pedir el de la ventana siguiente. Esa entra dentro de la tolerancia de reloj (mas menos una) y es
# posterior a la ya apuntada, asi que se acepta tanto si el reloj ya cambio de ventana como si no.
function CodigoTotp([string]$secretoEnBase32, [int]$desplazamientoDeVentanas = 0) {
    $llave = ConvertirDeBase32 $secretoEnBase32

    # [math]::Floor y NO un casting a [long]: en PowerShell la division da un decimal y convertirlo a
    # entero REDONDEA, asi que a partir del segundo 15 de cada ventana saldria la siguiente y el
    # codigo no coincidiria. Es un fallo que aparece la mitad de las veces, que es la peor forma de
    # aparecer.
    $ventana = [long][math]::Floor([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() / 30) + $desplazamientoDeVentanas

    # El contador va en ocho bytes con el mas significativo primero. El orden es parte del formato:
    # invertirlo produce codigos que no coinciden con los de ningun telefono ni con los del servidor.
    $contador = [BitConverter]::GetBytes($ventana)
    if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($contador) }

    $hmac = New-Object System.Security.Cryptography.HMACSHA1
    $hmac.Key = $llave
    $firma = $hmac.ComputeHash($contador)

    # Truncamiento dinamico del RFC 4226: los cuatro bits bajos del ultimo byte dicen desde donde leer
    # los cuatro que forman el numero, y el bit mas alto se apaga para que no salga negativo.
    $desplazamiento = $firma[$firma.Length - 1] -band 0x0F
    $numero = ((([int]$firma[$desplazamiento]) -band 0x7F) -shl 24) `
              -bor (([int]$firma[$desplazamiento + 1]) -shl 16) `
              -bor (([int]$firma[$desplazamiento + 2]) -shl 8) `
              -bor ([int]$firma[$desplazamiento + 3])

    return ($numero % 1000000).ToString("D6")
}
