using System;
using UnityEngine;

namespace Pokemon3D
{
    // Obliga a que este script esté acompañado del script 'Game' y que solo haya uno por objeto.
    [DisallowMultipleComponent, RequireComponent(typeof(Game))]
    public sealed class WorldLoader : MonoBehaviour
    {
        // -------------------------------------------------------------
        // EL SINGLETON (El "Cartel de Neón")
        // Cualquier otro script puede acceder al nivel escribiendo: AuthoredWorld.Instancia
        // -------------------------------------------------------------
        public static WorldLoader Instancia { get; private set; }

        public const int CURRENT_VERSION = 1;
        [SerializeField] int version = CURRENT_VERSION;
        public int Version { get { return version; } }

        // Awake se ejecuta al instante en cuanto el objeto aparece en el juego
        private void Awake()
        {
            // Si la Instancia está vacía, significa que somos el primer nivel en cargar
            if (Instancia == null)
            {
                Instancia = this; // "¡Yo soy el nivel oficial!"
            }
            // Si ya hay alguien ocupando el puesto de Instancia y no soy yo...
            else if (Instancia != this)
            {
                // ...damos un error para avisar de que hay un nivel duplicado.
                throw new InvalidOperationException("¡Error! Ya hay otro mundo de cargado a la vez.");
            }
        }

        // -------------------------------------------------------------
        // 1. BUSCAR EL NIVEL ACTUAL (Ahora es súper rápido gracias al Singleton)
        // -------------------------------------------------------------
        public static Game OwnerInLoadedScenes()
        {
            // En lugar de buscar por todo el juego perdiendo tiempo,
            // simplemente miramos si nuestro Singleton existe.
            if (Instancia != null)
            {
                return Instancia.GetComponent<Game>(); // Devolvemos su script 'Game'
            }

            return null; // Si no hay nivel, no devolvemos nada
        }

        // -------------------------------------------------------------
        // 2. CONECTAR TODO AL DARLE AL PLAY
        // -------------------------------------------------------------
        public void BindRuntime(Game game)
        {
            // Comprobación de seguridad: ¿Es el mismo juego y la versión correcta?
            if (game.gameObject != gameObject || version != CURRENT_VERSION)
                throw new InvalidOperationException("Error de vinculación con el mundo.");

            // Comprobación: ¿Están el jugador y la cámara puestos en el mapa?
            if (!game.player || !game.cam)
                throw new InvalidOperationException("¡Te falta poner al jugador o la cámara en el mapa guardado!");

            // 1. Despertamos al jugador y le decimos a la cámara que lo siga
            game.player.BindRuntime();
            game.cam.BindRuntime(game.player.transform);

            // 2. Encendemos las animaciones del jugador (su esqueleto procedural)
            var playerAnimator = game.player.GetComponent<Locomotion.ProceduralBodyAnimator>();
            if (playerAnimator && playerAnimator.SavedRig != null)
            {
                playerAnimator.BindSavedRig();
            }

            // 3. Vaciamos la lista de criaturas para empezar de cero
            game.creatures.Clear();

            // 4. Buscamos todas las criaturas que has puesto en la escena
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                foreach (var creature in root.GetComponentsInChildren<Creature>(true))
                {
                    // Despertamos a la criatura y la anotamos en la lista
                    creature.BindRuntime();
                    game.creatures.Add(creature);

                    // Obligamos a que la criatura tenga su esqueleto de animaciones
                    var animator = creature.GetComponent<Locomotion.ProceduralBodyAnimator>();
                    if (!animator || animator.SavedRig == null)
                        throw new InvalidOperationException("¡Hay una criatura que no tiene su esqueleto de animación configurado!");

                    // Encendemos las animaciones de la criatura


                    animator.BindSavedRig();
                }
            }
        }
    }
}
