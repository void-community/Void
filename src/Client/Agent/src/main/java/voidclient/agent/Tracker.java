package voidclient.agent;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.lang.instrument.Instrumentation;
import java.lang.ref.WeakReference;
import java.lang.reflect.Array;
import java.lang.reflect.Field;
import java.lang.reflect.GenericArrayType;
import java.lang.reflect.Method;
import java.lang.reflect.Modifier;
import java.lang.reflect.ParameterizedType;
import java.lang.reflect.Type;
import java.lang.reflect.TypeVariable;
import java.lang.reflect.WildcardType;
import java.util.ArrayList;
import java.util.Collection;
import java.util.Collections;
import java.util.IdentityHashMap;
import java.util.Iterator;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.WeakHashMap;

public final class Tracker {
    private static final String GameProfileName = "com.mojang.authlib.GameProfile";
    private static final Map<ClassLoader, Map<String, PositionSource[]>> LoaderPositionSources = Collections.synchronizedMap(new WeakHashMap<ClassLoader, Map<String, PositionSource[]>>());
    private static final Map<String, PositionSource[]> BootstrapPositionSources = new java.util.HashMap<String, PositionSource[]>();
    private static final Map<ClassLoader, Map<String, RotationSources>> LoaderRotationSources = Collections.synchronizedMap(new WeakHashMap<ClassLoader, Map<String, RotationSources>>());
    private static final Map<String, RotationSources> BootstrapRotationSources = new java.util.HashMap<String, RotationSources>();
    private static final List<WeakReference<Object>> ObservedPlayers = Collections.synchronizedList(new ArrayList<WeakReference<Object>>());
    private static Instrumentation instrumentation;
    private static String expectedName;
    private static volatile Object localPlayerValue;
    private static volatile WeakReference<Object> clientReference = new WeakReference<Object>(null);

    private Tracker() {
    }

    static void initialize(Instrumentation value, String name) {
        instrumentation = value;
        // The game's own --username is the name it plays as, however the launcher chose or spelled it.
        String launchedName = usernameFromCommand(System.getProperty("sun.java.command"));
        expectedName = launchedName != null ? launchedName : name;
    }

    static String usernameFromCommand(String command) {
        if (command == null)
            return null;

        String[] arguments = command.trim().split("\\s+");

        for (int index = 0; index < arguments.length; index++) {
            if ("--username".equals(arguments[index]))
                return index + 1 < arguments.length ? arguments[index + 1] : null;

            if (arguments[index].startsWith("--username="))
                return arguments[index].substring("--username=".length());
        }

        return null;
    }

    static void registerPositionSources(ClassLoader loader, String className, PositionSource[] sources) {
        synchronized (LoaderPositionSources) {
            Map<String, PositionSource[]> sourcesByClass;

            if (loader == null) {
                sourcesByClass = BootstrapPositionSources;
            } else {
                sourcesByClass = LoaderPositionSources.get(loader);

                if (sourcesByClass == null) {
                    sourcesByClass = new java.util.HashMap<String, PositionSource[]>();
                    LoaderPositionSources.put(loader, sourcesByClass);
                }
            }

            sourcesByClass.put(className, sources);
        }
    }

    public static void registerPlayer(Object value) {
        if (value != null) {
            ObservedPlayers.add(new WeakReference<Object>(value));
        }
    }

    static void bindClient(Object client) {
        if (clientReference.get() != client)
            clientReference = new WeakReference<Object>(client);
    }

    static Object activePlayer(Object client) {
        if (client == null)
            return null;
        List<Field> clientFields = fieldsOf(client.getClass());
        for (Field field : clientFields) {
            if (Modifier.isStatic(field.getModifiers()) || field.getType().isPrimitive())
                continue;
            Object player = readField(field, client);
            Object profile = player == null ? null : findProfile(player);
            if (profile == null || !belongsToCurrentWorld(client, clientFields, player))
                continue;
            // Without a known name, the client's own player is the one it holds in a player-typed field; entity-typed fields such as
            // the crosshair target can hold other players.
            boolean expected = expectedName == null ? declaresProfile(field.getType()) : expectedName.equals(profileName(profile));
            if (expected)
                return player;
        }
        return null;
    }

    /**
     * Describes why the client's own player in the current world is not the expected one, such as a server that renamed it.
     *
     * @return the description, or {@code null} when there is no such player
     */
    static String describeUnexpectedPlayer(Object client) {
        if (client == null || expectedName == null)
            return null;
        List<Field> clientFields = fieldsOf(client.getClass());
        for (Field field : clientFields) {
            if (Modifier.isStatic(field.getModifiers()) || !declaresProfile(field.getType()))
                continue;
            Object player = readField(field, client);
            Object profile = player == null ? null : findProfile(player);
            String name = profile == null ? null : profileName(profile);
            if (name != null && !expectedName.equals(name) && belongsToCurrentWorld(client, clientFields, player))
                return "Minecraft plays as " + name + " instead of the expected player " + expectedName;
        }
        return null;
    }

    private static boolean belongsToCurrentWorld(Object client, List<Field> clientFields, Object player) {
        for (Field playerField : fieldsOf(player.getClass())) {
            if (Modifier.isStatic(playerField.getModifiers()) || playerField.getType().isPrimitive())
                continue;
            Object world = readField(playerField, player);
            if (world == null)
                continue;
            boolean currentWorld = false;
            for (Field clientField : clientFields)
                if (!Modifier.isStatic(clientField.getModifiers()) && readField(clientField, client) == world)
                    currentWorld = true;
            if (!currentWorld)
                continue;
            for (Field worldField : fieldsOf(world.getClass()))
                if (!Modifier.isStatic(worldField.getModifiers()) && containsIdentity(valuesOf(readField(worldField, world)), player))
                    return true;
        }
        return false;
    }

    private static boolean declaresProfile(Class<?> type) {
        for (Field field : fieldsOf(type))
            if (!Modifier.isStatic(field.getModifiers()) && GameProfileName.equals(field.getType().getName()))
                return true;
        return false;
    }

    static String snapshotJson() {
        try {
            List<PlayerCandidate> candidates = collectCandidates();
            String unexpectedPlayer = candidates.isEmpty() ? describeUnexpectedPlayer(clientReference.get()) : null;

            if (unexpectedPlayer != null)
                return error("notInWorld", unexpectedPlayer);

            if (candidates.isEmpty())
                return error("notInWorld", "The client has no current player world");

            PlayerCandidate localPlayer = findLocalPlayer(candidates);

            if (localPlayer == null)
                return error("notInWorld", "The local player is not available");

            localPlayerValue = localPlayer.value;
            List<PlayerCandidate> trackedPlayers = findTrackedPlayers(localPlayer, candidates);

            if (trackedPlayers.isEmpty())
                return error("notInWorld", "The client world player collection is not available");

            StringBuilder json = new StringBuilder();
            json.append("{\"status\":\"ok\",\"local\":");
            appendPlayer(json, localPlayer);
            json.append(",\"remote\":[");
            boolean first = true;
            List<PlayerCandidate> exposedPlayers = new ArrayList<PlayerCandidate>();

            for (PlayerCandidate player : trackedPlayers) {
                if (player.value == localPlayer.value || samePlayerIdentity(player.id, player.name, localPlayer.id, localPlayer.name))
                    continue;

                boolean alreadyExposed = false;

                for (PlayerCandidate exposedPlayer : exposedPlayers) {
                    if (samePlayerIdentity(player.id, player.name, exposedPlayer.id, exposedPlayer.name)) {
                        alreadyExposed = true;
                        break;
                    }
                }

                if (alreadyExposed)
                    continue;

                if (!first)
                    json.append(',');

                appendPlayer(json, player);
                exposedPlayers.add(player);
                first = false;
            }

            json.append("]}");
            return json.toString();
        } catch (Throwable exception) {
            return error("unavailable", exception.getMessage() == null ? exception.getClass().getName() : exception.getMessage());
        }
    }

    private static List<PlayerCandidate> collectCandidates() {
        List<PlayerCandidate> candidates = new ArrayList<PlayerCandidate>();
        Object client = clientReference.get();
        if (client != null) {
            Object player = activePlayer(client);
            PlayerCandidate candidate = player == null ? null : createCandidate(player);
            if (candidate != null)
                candidates.add(candidate);
            return candidates;
        }
        Object cachedLocalPlayer = localPlayerValue;

        if (cachedLocalPlayer != null) {
            PlayerCandidate candidate = createCandidate(cachedLocalPlayer);

            if (candidate != null) {
                candidates.add(candidate);
                return candidates;
            }

            localPlayerValue = null;
        }

        IdentityHashMap<Object, Boolean> observed = new IdentityHashMap<Object, Boolean>();

        synchronized (ObservedPlayers) {
            Iterator<WeakReference<Object>> iterator = ObservedPlayers.iterator();

            while (iterator.hasNext()) {
                Object value = iterator.next().get();

                if (value == null) {
                    iterator.remove();
                    continue;
                }

                if (observed.put(value, Boolean.TRUE) != null)
                    continue;

                PlayerCandidate candidate = createCandidate(value);

                if (candidate != null)
                    candidates.add(candidate);
            }
        }

        if (findLocalPlayer(candidates) != null)
            return candidates;

        for (Class<?> type : instrumentation.getAllLoadedClasses()) {
            try {
                collectCandidatesFromSingleton(type, candidates, observed);
            } catch (Throwable exception) {
                // Some optional library classes cannot resolve every declared field at runtime.
            }
        }

        return candidates;
    }

    private static void collectCandidatesFromSingleton(Class<?> type, List<PlayerCandidate> candidates, IdentityHashMap<Object, Boolean> observed) {
        for (Field singletonField : type.getDeclaredFields()) {
            if (!Modifier.isStatic(singletonField.getModifiers()) || singletonField.getType() != type)
                continue;

            Object singleton = readField(singletonField, null);

            if (singleton == null)
                continue;

            for (Field field : fieldsOf(singleton.getClass())) {
                if (Modifier.isStatic(field.getModifiers()) || field.getType().isPrimitive())
                    continue;

                Object value = readField(field, singleton);

                if (value == null || observed.put(value, Boolean.TRUE) != null)
                    continue;

                PlayerCandidate candidate = createCandidate(value);

                if (candidate != null)
                    candidates.add(candidate);
            }
        }
    }

    private static PlayerCandidate createCandidate(Object value) {
        Object profile = findProfile(value);

        if (profile == null)
            return null;

        PositionReader reader = findPositionReader(value.getClass());
        RotationReader rotationReader = findRotationReader(value.getClass());

        if (reader == null || rotationReader == null)
            return null;

        double[] position = reader.read(value);
        double[] rotation = rotationReader.read(value);

        for (double coordinate : position) {
            if (!Double.isFinite(coordinate))
                throw new IllegalStateException("A tracked player has a non-finite coordinate");
        }

        for (double angle : rotation) {
            if (!Double.isFinite(angle))
                throw new IllegalStateException("A tracked player has a non-finite rotation");
        }

        return new PlayerCandidate(value, profileId(profile), profileName(profile), position, rotation);
    }

    private static PlayerCandidate findLocalPlayer(List<PlayerCandidate> candidates) {
        if (expectedName != null) {
            for (PlayerCandidate candidate : candidates) {
                if (expectedName.equals(candidate.name))
                    return candidate;
            }
        }

        return candidates.isEmpty() ? null : candidates.get(0);
    }

    private static List<PlayerCandidate> findTrackedPlayers(PlayerCandidate localPlayer, List<PlayerCandidate> candidates) {
        IdentityHashMap<Object, PlayerCandidate> candidatesByIdentity = new IdentityHashMap<Object, PlayerCandidate>();

        for (PlayerCandidate candidate : candidates)
            candidatesByIdentity.put(candidate.value, candidate);

        List<PlayerCandidate> bestTrackedPlayers = null;
        int bestSpecificity = -1;
        boolean ambiguousDeclaredPlayerCollection = false;

        for (Field localField : fieldsOf(localPlayer.value.getClass())) {
            if (Modifier.isStatic(localField.getModifiers()) || localField.getType().isPrimitive())
                continue;

            Object possibleWorld = readField(localField, localPlayer.value);

            if (possibleWorld == null || possibleWorld.getClass().getName().startsWith("java."))
                continue;

            for (Field worldField : fieldsOf(possibleWorld.getClass())) {
                if (Modifier.isStatic(worldField.getModifiers()))
                    continue;

                Object container = readField(worldField, possibleWorld);
                List<Object> values = valuesOf(container);

                if (!containsIdentity(values, localPlayer.value))
                    continue;

                boolean declaredPlayerCollection = hasPlayerElementType(worldField);
                List<PlayerCandidate> trackedPlayers = readPlayerCollection(values, candidatesByIdentity, declaredPlayerCollection);

                if (trackedPlayers == null)
                    continue;

                if (hasDuplicatePlayerIdentity(trackedPlayers)) {
                    ambiguousDeclaredPlayerCollection |= declaredPlayerCollection;
                    continue;
                }

                int specificity = declaredPlayerCollection ? 1 : 0;

                if (specificity > bestSpecificity || specificity == bestSpecificity && (bestTrackedPlayers == null || trackedPlayers.size() > bestTrackedPlayers.size())) {
                    bestTrackedPlayers = trackedPlayers;
                    bestSpecificity = specificity;
                }
            }
        }

        if (bestTrackedPlayers != null)
            return bestTrackedPlayers;

        if (ambiguousDeclaredPlayerCollection)
            throw new IllegalStateException("The client player collection contains duplicate player identities");

        return Collections.emptyList();
    }

    private static List<PlayerCandidate> readPlayerCollection(List<Object> values, IdentityHashMap<Object, PlayerCandidate> candidatesByIdentity, boolean declaredPlayerCollection) {
        List<PlayerCandidate> players = new ArrayList<PlayerCandidate>();

        for (Object value : values) {
            if (value == null)
                continue;

            PlayerCandidate candidate = candidatesByIdentity.get(value);

            if (candidate == null) {
                if (findProfile(value) == null) {
                    if (declaredPlayerCollection)
                        throw new IllegalStateException("The client player collection contains an unreadable player profile");

                    return null;
                }

                candidate = createTrackedPlayerCandidate(value);
                candidatesByIdentity.put(value, candidate);
            }

            players.add(candidate);
        }

        return players;
    }

    private static boolean hasDuplicatePlayerIdentity(List<PlayerCandidate> players) {
        for (int firstIndex = 0; firstIndex < players.size(); firstIndex++) {
            PlayerCandidate first = players.get(firstIndex);

            for (int secondIndex = firstIndex + 1; secondIndex < players.size(); secondIndex++) {
                PlayerCandidate second = players.get(secondIndex);

                if (first.value != second.value && samePlayerIdentity(first.id, first.name, second.id, second.name))
                    return true;
            }
        }

        return false;
    }

    private static boolean hasPlayerElementType(Field field) {
        try {
            return hasPlayerElementType(field.getGenericType(), 0);
        } catch (Throwable exception) {
            return false;
        }
    }

    static boolean hasPlayerElementType(Type type, int depth) {
        if (depth > 8)
            return false;

        if (type instanceof Class<?>) {
            for (Field field : fieldsOf((Class<?>) type)) {
                if (GameProfileName.equals(field.getType().getName()))
                    return true;
            }

            return false;
        }

        if (type instanceof ParameterizedType) {
            for (Type argument : ((ParameterizedType) type).getActualTypeArguments()) {
                if (hasPlayerElementType(argument, depth + 1))
                    return true;
            }

            return false;
        }

        if (type instanceof GenericArrayType)
            return hasPlayerElementType(((GenericArrayType) type).getGenericComponentType(), depth + 1);

        if (type instanceof TypeVariable<?>) {
            for (Type bound : ((TypeVariable<?>) type).getBounds()) {
                if (hasPlayerElementType(bound, depth + 1))
                    return true;
            }

            return false;
        }

        if (type instanceof WildcardType) {
            WildcardType wildcard = (WildcardType) type;

            for (Type bound : wildcard.getUpperBounds()) {
                if (hasPlayerElementType(bound, depth + 1))
                    return true;
            }

            for (Type bound : wildcard.getLowerBounds()) {
                if (hasPlayerElementType(bound, depth + 1))
                    return true;
            }
        }

        return false;
    }

    static boolean samePlayerIdentity(String firstId, String firstName, String secondId, String secondName) {
        if (firstId != null && secondId != null)
            return firstId.equals(secondId);

        return firstName != null && secondName != null && firstName.equals(secondName);
    }

    private static PlayerCandidate createTrackedPlayerCandidate(Object value) {
        Object profile = findProfile(value);

        if (profile == null)
            return null;

        PlayerCandidate candidate = createCandidate(value);

        if (candidate == null)
            throw new IllegalStateException("Could not resolve coordinates for a tracked player");

        return candidate;
    }

    private static boolean containsIdentity(List<Object> values, Object expected) {
        for (Object value : values) {
            if (value == expected)
                return true;
        }

        return false;
    }

    private static List<Object> valuesOf(Object container) {
        List<Object> values = new ArrayList<Object>();

        try {
            if (container instanceof Map<?, ?>)
                values.addAll(((Map<?, ?>) container).values());
            else if (container instanceof Collection<?> )
                values.addAll((Collection<?>) container);
            else if (container != null && container.getClass().isArray()) {
                int length = Array.getLength(container);

                for (int index = 0; index < length; index++)
                    values.add(Array.get(container, index));
            }
        } catch (RuntimeException exception) {
            return Collections.emptyList();
        }

        return values;
    }

    private static Object findProfile(Object value) {
        for (Field field : fieldsOf(value.getClass())) {
            if (GameProfileName.equals(field.getType().getName()))
                return readField(field, value);
        }

        return null;
    }

    private static PositionReader findPositionReader(Class<?> type) {
        List<Class<?>> hierarchy = new ArrayList<Class<?>>();

        for (Class<?> current = type; current != null; current = current.getSuperclass())
            hierarchy.add(current);

        for (int index = hierarchy.size() - 1; index >= 0; index--) {
            Class<?> current = hierarchy.get(index);
            PositionSource[] sources;

            synchronized (LoaderPositionSources) {
                Map<String, PositionSource[]> sourcesByClass = current.getClassLoader() == null ? BootstrapPositionSources : LoaderPositionSources.get(current.getClassLoader());
                sources = sourcesByClass == null ? null : sourcesByClass.get(current.getName().replace('.', '/'));
            }

            if (sources == null) {
                sources = discoverPositionSources(current);

                if (sources != null)
                    registerPositionSources(current.getClassLoader(), current.getName().replace('.', '/'), sources);
            }

            if (sources != null)
                return PositionReader.create(type.getClassLoader(), sources);
        }

        return null;
    }

    private static RotationReader findRotationReader(Class<?> type) {
        List<Class<?>> hierarchy = new ArrayList<Class<?>>();
        PositionSource bodyYaw = null;
        PositionSource headYaw = null;
        PositionSource headPitch = null;

        for (Class<?> current = type; current != null; current = current.getSuperclass())
            hierarchy.add(current);

        for (int index = hierarchy.size() - 1; index >= 0; index--) {
            Class<?> current = hierarchy.get(index);
            RotationSources sources;

            synchronized (LoaderRotationSources) {
                Map<String, RotationSources> sourcesByClass = current.getClassLoader() == null ? BootstrapRotationSources : LoaderRotationSources.get(current.getClassLoader());
                sources = sourcesByClass == null ? null : sourcesByClass.get(current.getName().replace('.', '/'));
            }

            if (sources == null) {
                sources = discoverRotationSources(current);

                if (sources != null)
                    registerRotationSources(current.getClassLoader(), current.getName().replace('.', '/'), sources);
            }

            if (sources == null)
                continue;

            if (bodyYaw == null)
                bodyYaw = sources.bodyYaw;

            if (headYaw == null)
                headYaw = sources.headYaw;

            if (headPitch == null)
                headPitch = sources.headPitch;

            RotationSources combined = new RotationSources(bodyYaw, headYaw, headPitch);

            if (combined.isComplete())
                return RotationReader.create(type.getClassLoader(), combined);
        }

        return null;
    }

    private static void registerRotationSources(ClassLoader loader, String className, RotationSources sources) {
        synchronized (LoaderRotationSources) {
            Map<String, RotationSources> sourcesByClass;

            if (loader == null) {
                sourcesByClass = BootstrapRotationSources;
            } else {
                sourcesByClass = LoaderRotationSources.get(loader);

                if (sourcesByClass == null) {
                    sourcesByClass = new java.util.HashMap<String, RotationSources>();
                    LoaderRotationSources.put(loader, sourcesByClass);
                }
            }

            sourcesByClass.put(className, sources);
        }
    }

    private static PositionSource[] discoverPositionSources(Class<?> type) {
        if (type.getName().startsWith("java."))
            return null;

        String resourceName = type.getName().replace('.', '/') + ".class";
        ClassLoader loader = type.getClassLoader();

        try (InputStream stream = loader == null ? ClassLoader.getSystemResourceAsStream(resourceName) : loader.getResourceAsStream(resourceName)) {
            if (stream == null)
                return null;

            ByteArrayOutputStream output = new ByteArrayOutputStream();
            byte[] buffer = new byte[8192];
            int count;

            while ((count = stream.read(buffer)) >= 0)
                output.write(buffer, 0, count);

            return PositionSourceDiscovery.findPositionSources(output.toByteArray());
        } catch (IOException exception) {
            return null;
        }
    }

    private static RotationSources discoverRotationSources(Class<?> type) {
        if (type.getName().startsWith("java."))
            return null;

        String resourceName = type.getName().replace('.', '/') + ".class";
        ClassLoader loader = type.getClassLoader();

        try (InputStream stream = loader == null ? ClassLoader.getSystemResourceAsStream(resourceName) : loader.getResourceAsStream(resourceName)) {
            if (stream == null)
                return null;

            ByteArrayOutputStream output = new ByteArrayOutputStream();
            byte[] buffer = new byte[8192];
            int count;

            while ((count = stream.read(buffer)) >= 0)
                output.write(buffer, 0, count);

            return RotationSourceDiscovery.findRotationSources(output.toByteArray());
        } catch (IOException exception) {
            return null;
        }
    }

    private static String profileId(Object profile) {
        Object value = invokeProfileMethod(profile, "getId", "id");
        return value instanceof UUID ? value.toString() : null;
    }

    private static String profileName(Object profile) {
        Object value = invokeProfileMethod(profile, "getName", "name");
        return value instanceof String ? (String) value : null;
    }

    private static Object invokeProfileMethod(Object profile, String firstName, String secondName) {
        for (String name : new String[] { firstName, secondName }) {
            try {
                Method method = profile.getClass().getMethod(name);
                return method.invoke(profile);
            } catch (ReflectiveOperationException exception) {
                // Try the next stable GameProfile accessor form.
            }
        }

        return null;
    }

    private static List<Field> fieldsOf(Class<?> type) {
        List<Field> fields = new ArrayList<Field>();

        for (Class<?> current = type; current != null; current = current.getSuperclass())
            Collections.addAll(fields, current.getDeclaredFields());

        return fields;
    }

    private static Object readField(Field field, Object instance) {
        try {
            if (!field.isAccessible())
                field.setAccessible(true);

            return field.get(instance);
        } catch (Throwable exception) {
            return null;
        }
    }

    static Double boundingBoxMinimumY(Object entity, double x, double z) {
        for (Field entityField : fieldsOf(entity.getClass())) {
            if (Modifier.isStatic(entityField.getModifiers()) || entityField.getType().isPrimitive())
                continue;

            Object possibleBox = readField(entityField, entity);

            if (possibleBox == null)
                continue;

            List<Field> coordinateFields = new ArrayList<Field>();

            for (Field field : fieldsOf(possibleBox.getClass())) {
                if (!Modifier.isStatic(field.getModifiers()) && field.getType() == double.class)
                    coordinateFields.add(field);
            }

            if (coordinateFields.size() != 6)
                continue;

            double[] coordinates = new double[6];
            boolean readable = true;

            for (int index = 0; index < coordinates.length; index++) {
                try {
                    Field field = coordinateFields.get(index);

                    if (!field.isAccessible())
                        field.setAccessible(true);

                    coordinates[index] = field.getDouble(possibleBox);
                    readable &= Double.isFinite(coordinates[index]);
                } catch (Throwable exception) {
                    readable = false;
                }
            }

            if (!readable)
                continue;

            double bestScore = Double.POSITIVE_INFINITY;
            Double minimumY = null;

            for (int firstX = 0; firstX < coordinates.length; firstX++) {
                for (int secondX = firstX + 1; secondX < coordinates.length; secondX++) {
                    for (int firstZ = 0; firstZ < coordinates.length; firstZ++) {
                        if (firstZ == firstX || firstZ == secondX)
                            continue;

                        for (int secondZ = firstZ + 1; secondZ < coordinates.length; secondZ++) {
                            if (secondZ == firstX || secondZ == secondX)
                                continue;

                            double xSpan = Math.abs(coordinates[firstX] - coordinates[secondX]);
                            double zSpan = Math.abs(coordinates[firstZ] - coordinates[secondZ]);
                            double score = Math.abs((coordinates[firstX] + coordinates[secondX]) / 2.0 - x) +
                                           Math.abs((coordinates[firstZ] + coordinates[secondZ]) / 2.0 - z) +
                                           Math.abs(xSpan - zSpan);

                            if (score >= bestScore)
                                continue;

                            bestScore = score;
                            minimumY = null;

                            for (int index = 0; index < coordinates.length; index++) {
                                if (index != firstX && index != secondX && index != firstZ && index != secondZ) {
                                    minimumY = minimumY == null ? coordinates[index] : Math.min(minimumY.doubleValue(), coordinates[index]);
                                }
                            }
                        }
                    }
                }
            }

            if (bestScore < 0.001 && minimumY != null)
                return minimumY;
        }

        return null;
    }

    private static void appendPlayer(StringBuilder json, PlayerCandidate player) {
        json.append("{\"uuid\":");
        appendString(json, player.id);
        json.append(",\"name\":");
        appendString(json, player.name);
        json.append(",\"position\":{\"x\":").append(player.position[0]);
        json.append(",\"y\":").append(player.position[1]);
        json.append(",\"z\":").append(player.position[2]).append('}');
        json.append(",\"body\":{\"yaw\":").append(player.rotation[0]).append('}');
        json.append(",\"head\":{\"yaw\":").append(player.rotation[1]);
        json.append(",\"pitch\":").append(player.rotation[2]).append('}');
        json.append('}');
    }

    private static String error(String status, String message) {
        StringBuilder json = new StringBuilder("{\"status\":");
        appendString(json, status);
        json.append(",\"message\":");
        appendString(json, message);
        return json.append('}').toString();
    }

    private static void appendString(StringBuilder json, String value) {
        if (value == null) {
            json.append("null");
            return;
        }

        json.append('"');

        for (int index = 0; index < value.length(); index++) {
            char character = value.charAt(index);

            if (character == '"' || character == '\\')
                json.append('\\').append(character);
            else if (character == '\n')
                json.append("\\n");
            else if (character == '\r')
                json.append("\\r");
            else if (character == '\t')
                json.append("\\t");
            else if (character < 0x20)
                json.append(String.format("\\u%04x", (int) character));
            else
                json.append(character);
        }

        json.append('"');
    }

    private static final class PlayerCandidate {
        private final Object value;
        private final String id;
        private final String name;
        private final double[] position;
        private final double[] rotation;

        private PlayerCandidate(Object value, String id, String name, double[] position, double[] rotation) {
            this.value = value;
            this.id = id;
            this.name = name;
            this.position = position;
            this.rotation = rotation;
        }
    }

    private static final class RotationReader {
        private final Field[] fields;
        private final Method[] methods;

        private RotationReader(Field[] fields, Method[] methods) {
            this.fields = fields;
            this.methods = methods;
        }

        private static RotationReader create(ClassLoader loader, RotationSources sources) {
            PositionSource[] sourceValues = new PositionSource[] { sources.bodyYaw, sources.headYaw, sources.headPitch };
            Field[] fields = new Field[sourceValues.length];
            Method[] methods = new Method[sourceValues.length];

            try {
                for (int index = 0; index < sourceValues.length; index++) {
                    PositionSource source = sourceValues[index];
                    Class<?> owner = Class.forName(source.owner.replace('/', '.'), false, loader);

                    if (source.method) {
                        methods[index] = owner.getDeclaredMethod(source.name);
                        methods[index].setAccessible(true);
                    } else {
                        fields[index] = owner.getDeclaredField(source.name);
                        fields[index].setAccessible(true);
                    }
                }

                return new RotationReader(fields, methods);
            } catch (ReflectiveOperationException exception) {
                throw new IllegalStateException("Could not resolve live player rotations", exception);
            }
        }

        private double[] read(Object value) {
            double[] result = new double[3];

            try {
                for (int index = 0; index < result.length; index++)
                    result[index] = fields[index] == null ? ((Number) methods[index].invoke(value)).doubleValue() : fields[index].getFloat(value);

                return result;
            } catch (ReflectiveOperationException exception) {
                throw new IllegalStateException("Could not read live player rotations", exception);
            }
        }
    }

    private static final class PositionReader {
        private final Field[] fields;
        private final Method[] methods;

        private PositionReader(Field[] fields, Method[] methods) {
            this.fields = fields;
            this.methods = methods;
        }

        private static PositionReader create(ClassLoader loader, PositionSource[] sources) {
            Field[] fields = new Field[3];
            Method[] methods = new Method[3];

            try {
                for (int index = 0; index < sources.length; index++) {
                    PositionSource source = sources[index];
                    Class<?> owner = Class.forName(source.owner.replace('/', '.'), false, loader);

                    if (source.method) {
                        methods[index] = owner.getDeclaredMethod(source.name);
                        methods[index].setAccessible(true);
                    } else {
                        fields[index] = owner.getDeclaredField(source.name);
                        fields[index].setAccessible(true);
                    }
                }

                return new PositionReader(fields, methods);
            } catch (ReflectiveOperationException exception) {
                throw new IllegalStateException("Could not resolve live player coordinates", exception);
            }
        }

        private double[] read(Object value) {
            double[] result = new double[3];

            try {
                for (int index = 0; index < result.length; index++)
                    result[index] = fields[index] == null ? ((Number) methods[index].invoke(value)).doubleValue() : fields[index].getDouble(value);

                Double minimumY = boundingBoxMinimumY(value, result[0], result[2]);

                if (minimumY != null)
                    result[1] = minimumY.doubleValue();

                return result;
            } catch (ReflectiveOperationException exception) {
                throw new IllegalStateException("Could not read live player coordinates", exception);
            }
        }
    }
}
