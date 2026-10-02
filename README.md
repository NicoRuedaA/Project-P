# Wildbound — zona jugable para Unity

Prototipo en tercera persona con criaturas originales provisionales. Unity **2022.3.9f1**, renderizador Built-in. No requiere assets externos ni paquetes de pago.

## Abrir
1. Descomprime este ZIP.
2. Unity Hub → Add project from disk → selecciona `Wildbound` (contiene Assets, Packages y ProjectSettings).
3. Abre con Unity 2022.3.9f1. El editor crea `Assets/Scenes/Valley.unity` automáticamente después de compilar.
4. Abre esa escena y pulsa **Play**. Si no se genera, usa **Wildbound → Create playable valley**.
5. Para un ejecutable, selecciona tu plataforma en File → Build Settings y usa **Wildbound → Build desktop**. Instala antes el módulo de esa plataforma en Unity Hub.

## Controles
| Acción | Control |
|---|---|
| Mover / cámara | WASD / ratón |
| Correr / saltar / esquivar | Shift / Espacio / Ctrl izquierdo |
| Apuntar / lanzar cápsula | Botón derecho / R o clic izquierdo al apuntar |
| Fijar objetivo / cancelar orden | TAB / Q |
| Invocar o recoger compañero | E |
| Pulso / barrido / atadura / recuperación | 1 / 2 / 3 / 4 |
| Equipo / seleccionar criatura | I / flechas izquierda y derecha |
| Recuperar equipo y cápsulas en campamento | F cerca del cristal |
| Guardar / cargar | F5 / F9 |
| Pausa | Esc |

## Qué hay
Valle de aproximadamente 150 × 150 metros, bosque, lago decorativo transitable, meseta con rampa, campamento y nueve encuentros iniciales de tres especies. El compañero inicial es Bramble. Las criaturas agresivas atacan al entrenador o compañero. Puedes esquivar, ordenar ataques y capturar sin entrar en un modo por turnos. Las cápsulas y ataques tienen colisión; las habilidades tienen preparación y recarga. Los derrotados conceden experiencia al compañero. El campamento recupera al equipo y repone hasta un mínimo de 20 cápsulas. Reposición gradual de criaturas salvajes.

Guardado JSON local en `Application.persistentDataPath/wildbound-save.json`: equipo, niveles, XP, salud de criaturas, cápsulas, selección y posición del entrenador. No persiste el estado de cada criatura salvaje ni la salud del entrenador; al cargar se recupera el entrenador.

## Alcance y validación
Escenario y personajes construidos por código con primitivas, materiales toon y animación procedural simple. Es una base funcional de prototipo, no un juego final ni una reproducción gráfica de BOTW. El lago es visual: no hay natación. No incluye navegación NavMesh, escalada, planeador, audio, mando, multijugador, streaming de mundo o animaciones esqueléticas. La IA usa dirección directa y CharacterController; puede atascarse detrás de obstáculos.

El entorno de creación no dispone de Unity: no se ha ejecutado Play Mode ni producido un ejecutable. Se han revisado estructura y sintaxis de C#; deben completarse las pruebas de `Documentation/PLAYTEST.md` en Unity.

## Referencias y permisos
El usuario declara permiso explícito de los dos autores. Las implementaciones originales de lanzamiento y probabilidad de PokemonCatcher se conservan en Documentation/References (fuera de Assets). Su mecánica de raycast cámara → punto de lanzamiento y probabilidad especie × modificadores se adapta en Game.cs. Se corrige la colección mediante registros independientes de los GameObjects destruidos.

- exemplerie/PokemonCatcher: https://github.com/exemplerie/PokemonCatcher
- B3n00n/Open-World-Pokemon: https://github.com/B3n00n/Open-World-Pokemon

Open-World-Pokemon se utiliza como referencia de organización (tercera persona, objetivo, equipo y progresión); no se copia su combate por turnos ni sus assets. No se han fusionado los proyectos completos: se construye un proyecto autocontenido y se trasladan las mecánicas relevantes con los cambios necesarios para tiempo real. No se distribuyen assets Pokémon ni paquetes de terceros de los repos.
