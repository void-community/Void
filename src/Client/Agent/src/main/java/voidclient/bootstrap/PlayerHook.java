package voidclient.bootstrap;

import java.util.function.Consumer;

/**
 * Receives the registration calls PlayerTransformer injects into game and mod constructors. The agent loads this class from the
 * bootstrap class path because mod loaders such as NeoForge 1.21.1 define those classes in loaders that can't see the agent jar,
 * while every loader still delegates to the bootstrap loader. It must therefore depend on nothing but JDK classes.
 */
public final class PlayerHook {
    private static volatile Consumer<Object> registration;

    private PlayerHook() {
    }

    public static void install(Consumer<Object> value) {
        registration = value;
    }

    public static void registerPlayer(Object value) {
        Consumer<Object> current = registration;

        if (current == null)
            return;

        try {
            current.accept(value);
        } catch (Throwable exception) {
            // Tracking one object is never worth failing the constructor of a game or mod class.
        }
    }
}
