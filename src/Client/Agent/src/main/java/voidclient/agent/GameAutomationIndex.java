package voidclient.agent;

import java.io.File;
import java.io.IOException;
import java.io.InputStream;
import java.net.URI;
import java.net.URL;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.jar.JarEntry;
import java.util.jar.JarFile;
import org.objectweb.asm.ClassReader;
import org.objectweb.asm.Opcodes;
import org.objectweb.asm.Type;
import org.objectweb.asm.tree.AbstractInsnNode;
import org.objectweb.asm.tree.ClassNode;
import org.objectweb.asm.tree.FieldInsnNode;
import org.objectweb.asm.tree.FieldNode;
import org.objectweb.asm.tree.InsnNode;
import org.objectweb.asm.tree.IntInsnNode;
import org.objectweb.asm.tree.JumpInsnNode;
import org.objectweb.asm.tree.LdcInsnNode;
import org.objectweb.asm.tree.MethodInsnNode;
import org.objectweb.asm.tree.MethodNode;
import org.objectweb.asm.tree.TypeInsnNode;
import org.objectweb.asm.tree.VarInsnNode;

final class GameAutomationIndex {
    private static final Map<String, IndexedCode> IndexedLocations = new HashMap<String, IndexedCode>();

    private GameAutomationIndex() {
    }

    static synchronized IndexedCode index(URL location) {
        if (location == null || !"file".equals(location.getProtocol()))
            return null;

        String key = location.toExternalForm();
        if (IndexedLocations.containsKey(key))
            return IndexedLocations.get(key);

        try {
            Map<String, ClassNode> types = readTypes(location.toURI());
            IndexedCode indexed = build(types);

            IndexedLocations.put(key, indexed);

            return indexed;
        } catch (Throwable exception) {
            // Remember the failure; re-reading the whole location for every loaded class stalls game startup.
            IndexedLocations.put(key, null);
            GameAutomationController.recordIndexFailure(key, exception.getClass().getName() + ": " + exception.getMessage());
            return null;
        }
    }

    private static Map<String, ClassNode> readTypes(URI location) throws Exception {
        File file = new File(location);
        Map<String, ClassNode> types = new HashMap<String, ClassNode>();

        if (file.isDirectory()) {
            readDirectory(types, file, file);
            return types;
        }

        JarFile jar = new JarFile(file);

        try {
            java.util.Enumeration<JarEntry> entries = jar.entries();

            while (entries.hasMoreElements()) {
                JarEntry entry = entries.nextElement();

                if (!entry.getName().endsWith(".class"))
                    continue;

                InputStream input = jar.getInputStream(entry);

                try {
                    addType(types, input);
                } finally {
                    input.close();
                }
            }
        } finally {
            jar.close();
        }

        return types;
    }

    private static void readDirectory(Map<String, ClassNode> types, File root, File directory) throws IOException {
        File[] files = directory.listFiles();

        if (files == null)
            return;

        for (File file : files) {
            if (file.isDirectory()) {
                readDirectory(types, root, file);
            } else if (file.getName().endsWith(".class")) {
                java.io.FileInputStream input = new java.io.FileInputStream(file);

                try {
                    addType(types, input);
                } finally {
                    input.close();
                }
            }
        }
    }

    private static void addType(Map<String, ClassNode> types, InputStream input) throws IOException {
        ClassNode type = new ClassNode();
        new ClassReader(input).accept(type, ClassReader.SKIP_DEBUG | ClassReader.SKIP_FRAMES);
        types.put(type.name, type);
    }

    private static IndexedCode build(Map<String, ClassNode> types) {
        List<DirectCandidate> directCandidates = new ArrayList<DirectCandidate>();

        for (ClassNode type : types.values()) {
            DirectConnectPlan direct = DirectConnectDiscovery.discover(type);

            if (direct != null)
                directCandidates.add(new DirectCandidate(type, direct));
        }

        if (directCandidates.isEmpty())
            return null;

        if (directCandidates.size() > 1) {
            int largestScreenFamily = 0;

            for (DirectCandidate candidate : directCandidates)
                largestScreenFamily = Math.max(largestScreenFamily, countSubtypes(types, candidate.type.superName));

            List<DirectCandidate> screenCandidates = new ArrayList<DirectCandidate>();

            for (DirectCandidate candidate : directCandidates) {
                if (countSubtypes(types, candidate.type.superName) == largestScreenFamily)
                    screenCandidates.add(candidate);
            }

            directCandidates = screenCandidates;
        }

        if (directCandidates.size() > 1) {
            int minimumTextFields = Integer.MAX_VALUE;

            for (DirectCandidate candidate : directCandidates)
                minimumTextFields = Math.min(minimumTextFields, countFields(candidate.type, candidate.plan.textFieldDescriptor));

            List<DirectCandidate> narrowCandidates = new ArrayList<DirectCandidate>();

            for (DirectCandidate candidate : directCandidates) {
                if (countFields(candidate.type, candidate.plan.textFieldDescriptor) == minimumTextFields)
                    narrowCandidates.add(candidate);
            }

            directCandidates = narrowCandidates;
        }

        if (directCandidates.size() != 1) {
            List<String> descriptions = new ArrayList<String>();

            for (DirectCandidate candidate : directCandidates)
                descriptions.add(candidate.plan.describe());

            GameAutomationController.recordIndexFailure("game-code", "Expected one structural Direct Connect screen but found " + directCandidates.size() + ": " + descriptions);
            return null;
        }

        DirectCandidate directCandidate = directCandidates.get(0);
        String screenBaseName = directCandidate.type.superName;
        Map<String, String> superTypes = new HashMap<String, String>();

        for (ClassNode type : types.values())
            superTypes.put(type.name, type.superName);

        ScreenSetter screenSetter = discoverScreenSetter(types, superTypes, screenBaseName);

        if (screenSetter == null) {
            GameAutomationController.recordIndexFailure("game-code", "No unique screen setter was found for " + screenBaseName);
            return null;
        }

        Map<String, List<TransitionPlan>> transitions = discoverTransitions(types, superTypes, screenBaseName, screenSetter);
        ChatPlan chat = discoverChat(types, superTypes, screenBaseName, screenSetter);
        if (chat == null) {
            GameAutomationController.recordIndexFailure("game-code", "No unique chat screen submission flow was found");
            return null;
        }

        FramePlan frame = ClientFrameDiscovery.discover(types, screenSetter.owner);
        PresentationOverlayPlan presentationOverlay = discoverPresentationOverlay(types, superTypes, screenSetter.owner, screenBaseName);
        Set<String> networkTypes = new HashSet<String>();
        for (String typeName : types.keySet())
            if (ConnectionFailureDiscovery.isNetworkHandler(types, typeName))
                networkTypes.add(typeName);
        AutomationPlan plan = new AutomationPlan(screenBaseName, screenSetter.owner, screenSetter.name,
            directCandidate.type.name,
            frame, ConnectionFailureDiscovery.discover(types), networkTypes,
            presentationOverlay, directCandidate.plan, chat, transitions);
        return new IndexedCode(plan, superTypes);
    }

    static PresentationOverlayPlan discoverPresentationOverlay(Map<String, ClassNode> types, Map<String, String> superTypes, String clientClassName, String screenBaseName) {
        ClassNode client = types.get(clientClassName);

        if (client == null)
            return null;

        List<FieldNode> screenFields = new ArrayList<FieldNode>();

        for (FieldNode field : client.fields) {
            String fieldType = objectType(field.desc);

            if ((field.access & Opcodes.ACC_STATIC) == 0 && fieldType != null && isScreen(fieldType, superTypes, screenBaseName))
                screenFields.add(field);
        }

        if (screenFields.size() != 1)
            throw new IllegalStateException("Expected one current-screen field in " + clientClassName + " but found " + screenFields.size());

        FieldNode screenField = screenFields.get(0);
        Set<String> screenInterfaces = interfacesOf(screenBaseName, types, new HashSet<String>());
        List<PresentationOverlayPlan> candidates = new ArrayList<PresentationOverlayPlan>();

        for (FieldNode field : client.fields) {
            String fieldType = objectType(field.desc);

            if ((field.access & Opcodes.ACC_STATIC) != 0 || fieldType == null
                || field.name.equals(screenField.name) || isScreen(fieldType, superTypes, screenBaseName)
                || !sharesInterface(fieldType, screenInterfaces, types)
                || !hasTrivialSetter(client, field)
                || countNullCheckedCoOccurrences(client, screenField, field) < 2)
                continue;

            candidates.add(new PresentationOverlayPlan(client.name, field.name, field.desc));
        }

        if (candidates.size() > 1) {
            List<String> descriptions = new ArrayList<String>();

            for (PresentationOverlayPlan candidate : candidates)
                descriptions.add(candidate.describe());

            throw new IllegalStateException("Expected at most one structural presentation overlay but found " + candidates.size() + ": " + descriptions);
        }

        return candidates.isEmpty() ? null : candidates.get(0);
    }

    private static int countNullCheckedCoOccurrences(ClassNode client, FieldNode screenField, FieldNode candidateField) {
        int count = 0;

        for (MethodNode method : client.methods) {
            if (readsFieldBeforeNullCheck(method, client.name, screenField.name, screenField.desc)
                && readsFieldBeforeNullCheck(method, client.name, candidateField.name, candidateField.desc))
                count++;
        }

        return count;
    }

    private static boolean readsFieldBeforeNullCheck(MethodNode method, String owner, String name, String descriptor) {
        for (AbstractInsnNode instruction = method.instructions.getFirst(); instruction != null; instruction = instruction.getNext()) {
            if (!(instruction instanceof FieldInsnNode) || instruction.getOpcode() != Opcodes.GETFIELD)
                continue;

            FieldInsnNode field = (FieldInsnNode) instruction;
            AbstractInsnNode next = nextReal(instruction);

            if (owner.equals(field.owner) && name.equals(field.name) && descriptor.equals(field.desc)
                && next instanceof JumpInsnNode && (next.getOpcode() == Opcodes.IFNULL || next.getOpcode() == Opcodes.IFNONNULL))
                return true;
        }

        return false;
    }

    private static boolean hasTrivialSetter(ClassNode client, FieldNode field) {
        String descriptor = '(' + field.desc + ")V";

        for (MethodNode method : client.methods) {
            if ((method.access & Opcodes.ACC_STATIC) != 0 || !descriptor.equals(method.desc))
                continue;

            int realInstructions = 0;
            boolean writesField = false;

            for (AbstractInsnNode instruction = method.instructions.getFirst(); instruction != null; instruction = instruction.getNext()) {
                if (instruction.getOpcode() < 0)
                    continue;

                realInstructions++;

                if (instruction instanceof FieldInsnNode && instruction.getOpcode() == Opcodes.PUTFIELD) {
                    FieldInsnNode writtenField = (FieldInsnNode) instruction;
                    writesField = client.name.equals(writtenField.owner) && field.name.equals(writtenField.name)
                        && field.desc.equals(writtenField.desc);
                }
            }

            if (writesField && realInstructions == 4)
                return true;
        }

        return false;
    }

    private static boolean sharesInterface(String typeName, Set<String> screenInterfaces, Map<String, ClassNode> types) {
        Set<String> candidateInterfaces = interfacesOf(typeName, types, new HashSet<String>());

        for (String interfaceName : candidateInterfaces) {
            if (!interfaceName.startsWith("java/") && screenInterfaces.contains(interfaceName))
                return true;
        }

        return false;
    }

    private static Set<String> interfacesOf(String typeName, Map<String, ClassNode> types, Set<String> visited) {
        if (typeName == null || !visited.add(typeName))
            return Collections.emptySet();

        ClassNode type = types.get(typeName);

        if (type == null)
            return Collections.emptySet();

        Set<String> result = new HashSet<String>();

        for (String interfaceName : type.interfaces) {
            result.add(interfaceName);
            result.addAll(interfacesOf(interfaceName, types, visited));
        }

        result.addAll(interfacesOf(type.superName, types, visited));
        return result;
    }

    private static String objectType(String descriptor) {
        Type type = Type.getType(descriptor);
        return type.getSort() == Type.OBJECT ? type.getInternalName() : null;
    }

    private static int countFields(ClassNode type, String descriptor) {
        int count = 0;

        for (FieldNode field : type.fields) {
            if (descriptor.equals(field.desc))
                count++;
        }

        return count;
    }

    private static int countSubtypes(Map<String, ClassNode> types, String baseName) {
        int count = 0;

        for (ClassNode type : types.values()) {
            String current = type.name;

            while (current != null) {
                if (baseName.equals(current)) {
                    count++;
                    break;
                }

                ClassNode parent = types.get(current);
                current = parent == null ? null : parent.superName;
            }
        }

        return count;
    }

    private static ScreenSetter discoverScreenSetter(Map<String, ClassNode> types, Map<String, String> superTypes, String screenBaseName) {
        Map<String, Integer> counts = new HashMap<String, Integer>();
        String descriptor = "(L" + screenBaseName + ";)V";

        for (ClassNode type : types.values()) {
            if (!isScreen(type.name, superTypes, screenBaseName))
                continue;

            for (MethodNode method : type.methods) {
                for (AbstractInsnNode instruction = method.instructions.getFirst(); instruction != null; instruction = instruction.getNext()) {
                    if (!(instruction instanceof MethodInsnNode))
                        continue;

                    MethodInsnNode call = (MethodInsnNode) instruction;

                    if (!descriptor.equals(call.desc))
                        continue;

                    String key = call.owner + '\t' + call.name + '\t' + call.desc;
                    counts.put(key, Integer.valueOf(counts.containsKey(key) ? counts.get(key).intValue() + 1 : 1));
                }
            }
        }

        String selected = null;
        int selectedCount = 0;

        for (Map.Entry<String, Integer> entry : counts.entrySet()) {
            if (entry.getValue().intValue() > selectedCount) {
                selected = entry.getKey();
                selectedCount = entry.getValue().intValue();
            }
        }

        if (selected == null)
            return null;

        String[] parts = selected.split("\\t", -1);
        return new ScreenSetter(parts[0], parts[1], parts[2]);
    }

    static Map<String, List<TransitionPlan>> discoverTransitions(Map<String, ClassNode> types,
                                                                         Map<String, String> superTypes,
                                                                         String screenBaseName,
                                                                         ScreenSetter screenSetter) {
        Map<String, List<TransitionPlan>> result = new HashMap<String, List<TransitionPlan>>();

        for (ClassNode type : types.values()) {
            String screenOwner = owningScreen(type, superTypes, screenBaseName);
            if (screenOwner == null)
                continue;

            for (MethodNode method : type.methods) {
                if ("<init>".equals(method.name) || "<clinit>".equals(method.name))
                    continue;

                for (AbstractInsnNode instruction = method.instructions.getFirst(); instruction != null; instruction = instruction.getNext()) {
                    if (!(instruction instanceof MethodInsnNode))
                        continue;

                    MethodInsnNode call = (MethodInsnNode) instruction;

                    if (!screenSetter.owner.equals(call.owner) || !screenSetter.name.equals(call.name)
                        || !screenSetter.descriptor.equals(call.desc))
                        continue;

                    List<String> targets = new ArrayList<String>();
                    AbstractInsnNode argument = previousReal(instruction);
                    boolean possibleConstructedTarget = argument != null && argument.getOpcode() != Opcodes.ACONST_NULL
                        && !isSelfReference(method, argument);
                    TypeInsnNode constructed = possibleConstructedTarget
                        ? findPreviousScreenNew(instruction, 48, superTypes, screenBaseName)
                        : null;
                    TransitionTargetKind targetKind = classifyTransitionTarget(method, argument, constructed);

                    if (constructed != null)
                        targets.add(constructed.desc);

                    ControlCondition condition = discoverControlCondition(method, instruction);
                    boolean enablesTransition = writesTrueBoolean(method);
                    TransitionPlan transition = new TransitionPlan(type.name, method.name, method.desc,
                        (method.access & Opcodes.ACC_STATIC) != 0, targets,
                        targetKind == TransitionTargetKind.PARENT, targetKind == TransitionTargetKind.SELF,
                        condition == null ? null : condition.owner,
                        condition == null ? null : condition.fieldName,
                        condition == null ? null : condition.id,
                        enablesTransition, usedArguments(method));
                    List<TransitionPlan> typeTransitions = result.get(screenOwner);

                    if (typeTransitions == null) {
                        typeTransitions = new ArrayList<TransitionPlan>();
                        result.put(screenOwner, typeTransitions);
                    }

                    mergeTransition(typeTransitions, transition);
                }
            }
        }

        return result;
    }

    private static String owningScreen(ClassNode type, Map<String, String> superTypes, String screenBaseName) {
        if (isScreen(type.name, superTypes, screenBaseName))
            return type.name;
        String owner = null;
        for (FieldNode field : type.fields) {
            String fieldType = objectType(field.desc);
            if ((field.access & Opcodes.ACC_SYNTHETIC) == 0 || !isScreen(fieldType, superTypes, screenBaseName))
                continue;
            if (owner != null && !owner.equals(fieldType))
                throw new IllegalStateException("Ambiguous screen owner for callback " + type.name);
            owner = fieldType;
        }
        return owner;
    }

    static boolean[] usedArguments(MethodNode method) {
        Type[] arguments = Type.getArgumentTypes(method.desc);
        boolean[] used = new boolean[arguments.length];
        int slot = (method.access & Opcodes.ACC_STATIC) == 0 ? 1 : 0;
        for (int index = 0; index < arguments.length; index++) {
            for (AbstractInsnNode instruction : method.instructions)
                if (instruction instanceof VarInsnNode && ((VarInsnNode) instruction).var == slot
                    && instruction.getOpcode() >= Opcodes.ILOAD && instruction.getOpcode() <= Opcodes.ALOAD)
                    used[index] = true;
            slot += arguments[index].getSize();
        }
        return used;
    }

    static TransitionTargetKind classifyTransitionTarget(MethodNode method, AbstractInsnNode argument, TypeInsnNode constructed) {
        if (argument != null && argument.getOpcode() == Opcodes.ACONST_NULL)
            return TransitionTargetKind.GAME;

        if (isSelfReference(method, argument))
            return TransitionTargetKind.SELF;

        return constructed == null ? TransitionTargetKind.PARENT : TransitionTargetKind.CONSTRUCTED;
    }

    private static boolean isSelfReference(MethodNode method, AbstractInsnNode argument) {
        return (method.access & Opcodes.ACC_STATIC) == 0
            && argument instanceof VarInsnNode
            && argument.getOpcode() == Opcodes.ALOAD
            && ((VarInsnNode) argument).var == 0;
    }

    private static void mergeTransition(List<TransitionPlan> transitions, TransitionPlan candidate) {
        for (TransitionPlan transition : transitions) {
            if (transition.owner.equals(candidate.owner) && transition.methodName.equals(candidate.methodName)
                && transition.methodDescriptor.equals(candidate.methodDescriptor)
                && transition.targetsParent == candidate.targetsParent
                && transition.targetsSelf == candidate.targetsSelf
                && java.util.Objects.equals(transition.controlId, candidate.controlId)) {
                for (String target : candidate.targetScreenNames) {
                    if (!transition.targetScreenNames.contains(target))
                        transition.targetScreenNames.add(target);
                }

                return;
            }
        }

        transitions.add(candidate);
    }

    private static ChatPlan discoverChat(Map<String, ClassNode> types, Map<String, String> superTypes,
                                         String screenBaseName, ScreenSetter screenSetter) {
        List<ChatPlan> candidates = new ArrayList<ChatPlan>();

        for (ClassNode type : types.values()) {
            if (!isScreen(type.name, superTypes, screenBaseName))
                continue;

            for (MethodNode method : type.methods) {
                if (!containsNullScreenTransition(method, screenSetter))
                    continue;

                MethodInsnNode getter = null;
                MethodInsnNode submit = null;
                FieldInsnNode textField = null;

                for (AbstractInsnNode instruction = method.instructions.getFirst(); instruction != null; instruction = instruction.getNext()) {
                    if (!(instruction instanceof MethodInsnNode))
                        continue;

                    MethodInsnNode call = (MethodInsnNode) instruction;

                    if ("()Ljava/lang/String;".equals(call.desc)) {
                        FieldInsnNode field = findPreviousField(call, type.name, call.owner, 12);

                        if (field != null) {
                            getter = call;
                            textField = field;
                        }
                    } else if (getter != null && acceptsLeadingString(call.desc) && !call.owner.startsWith("java/")) {
                        submit = call;
                        break;
                    }
                }

                if (getter == null || submit == null || textField == null)
                    continue;

                MethodInsnNode setter = findTextSetter(type, textField, getter.owner);
                ConstructorInvocation constructor = findConstruction(types, type.name);

                if (setter == null || constructor == null)
                    continue;

                ChatPlan candidate = new ChatPlan(type.name, constructor.descriptor, constructor.arguments,
                    textField.name, setter.owner, setter.name, getter.owner, getter.name,
                    submit.owner, submit.name, submit.desc);

                if (!containsChat(candidates, candidate))
                    candidates.add(candidate);
            }
        }

        return candidates.size() == 1 ? candidates.get(0) : null;
    }

    private static boolean acceptsLeadingString(String descriptor) {
        Type[] arguments = Type.getArgumentTypes(descriptor);
        return arguments.length > 0 && arguments[0].getSort() == Type.OBJECT
            && "java/lang/String".equals(arguments[0].getInternalName());
    }

    private static boolean containsNullScreenTransition(MethodNode method, ScreenSetter setter) {
        for (AbstractInsnNode instruction = method.instructions.getFirst(); instruction != null; instruction = instruction.getNext()) {
            if (instruction instanceof MethodInsnNode) {
                MethodInsnNode call = (MethodInsnNode) instruction;

                if (setter.owner.equals(call.owner) && setter.name.equals(call.name) && setter.descriptor.equals(call.desc)) {
                    AbstractInsnNode argument = previousReal(instruction);

                    if (argument != null && argument.getOpcode() == Opcodes.ACONST_NULL)
                        return true;
                }
            }
        }

        return false;
    }

    private static MethodInsnNode findTextSetter(ClassNode type, FieldInsnNode textField, String widgetOwner) {
        MethodInsnNode result = null;
        int resultScore = -1;

        for (MethodNode method : type.methods) {
            for (AbstractInsnNode instruction = method.instructions.getFirst(); instruction != null; instruction = instruction.getNext()) {
                if (!(instruction instanceof MethodInsnNode))
                    continue;

                MethodInsnNode call = (MethodInsnNode) instruction;

                if (!widgetOwner.equals(call.owner) || !"(Ljava/lang/String;)V".equals(call.desc))
                    continue;

                FieldInsnNode field = findPreviousField(call, type.name, widgetOwner, 20);

                if (field != null && textField.name.equals(field.name)) {
                    int score = findPreviousField(call, type.name, null, 12) == null ? 0 : 1;

                    if (score > resultScore) {
                        result = call;
                        resultScore = score;
                    } else if (score == resultScore && result != null && !result.name.equals(call.name)) {
                        return null;
                    }
                }
            }
        }

        return result;
    }

    private static ConstructorInvocation findConstruction(Map<String, ClassNode> types, String targetName) {
        List<ConstructorInvocation> candidates = new ArrayList<ConstructorInvocation>();

        for (ClassNode type : types.values()) {
            for (MethodNode method : type.methods) {
                for (AbstractInsnNode instruction = method.instructions.getFirst(); instruction != null; instruction = instruction.getNext()) {
                    if (!(instruction instanceof MethodInsnNode))
                        continue;

                    MethodInsnNode call = (MethodInsnNode) instruction;

                    if (call.getOpcode() != Opcodes.INVOKESPECIAL || !"<init>".equals(call.name) || !targetName.equals(call.owner))
                        continue;

                    Type[] arguments = Type.getArgumentTypes(call.desc);

                    if (arguments.length == 0 || arguments[0].getSort() != Type.OBJECT || !"java/lang/String".equals(arguments[0].getInternalName()))
                        continue;

                    Object[] values = new Object[arguments.length];
                    values[0] = "";
                    boolean supported = true;

                    for (int index = 1; index < arguments.length; index++) {
                        if (arguments[index].getSort() == Type.BOOLEAN)
                            values[index] = Boolean.TRUE;
                        else {
                            supported = false;
                            break;
                        }
                    }

                    if (supported)
                        candidates.add(new ConstructorInvocation(call.desc, values));
                }
            }
        }

        if (candidates.isEmpty())
            return null;

        ConstructorInvocation selected = candidates.get(0);

        for (ConstructorInvocation candidate : candidates) {
            if (Type.getArgumentTypes(candidate.descriptor).length < Type.getArgumentTypes(selected.descriptor).length)
                selected = candidate;
        }

        return selected;
    }

    private static FieldInsnNode findPreviousField(AbstractInsnNode start, String owner, String fieldTypeOwner, int maximumInstructions) {
        AbstractInsnNode instruction = start.getPrevious();

        for (int count = 0; instruction != null && count < maximumInstructions; instruction = instruction.getPrevious()) {
            if (instruction.getOpcode() < 0)
                continue;

            count++;

            if (instruction instanceof FieldInsnNode && instruction.getOpcode() == Opcodes.GETFIELD) {
                FieldInsnNode field = (FieldInsnNode) instruction;

                if (owner.equals(field.owner) && (fieldTypeOwner == null ? "Ljava/lang/String;".equals(field.desc) : ("L" + fieldTypeOwner + ";").equals(field.desc)))
                    return field;
            }
        }

        return null;
    }

    private static TypeInsnNode findPreviousScreenNew(AbstractInsnNode start, int maximumInstructions,
                                                      Map<String, String> superTypes, String screenBaseName) {
        AbstractInsnNode instruction = start.getPrevious();

        for (int count = 0; instruction != null && count < maximumInstructions; instruction = instruction.getPrevious()) {
            if (instruction.getOpcode() < 0)
                continue;

            count++;

            if (instruction instanceof TypeInsnNode && instruction.getOpcode() == Opcodes.NEW
                && isScreen(((TypeInsnNode) instruction).desc, superTypes, screenBaseName))
                return (TypeInsnNode) instruction;
        }

        return null;
    }

    private static ControlCondition discoverControlCondition(MethodNode method, AbstractInsnNode action) {
        Type[] arguments = Type.getArgumentTypes(method.desc);

        if (arguments.length != 1 || arguments[0].getSort() != Type.OBJECT)
            return null;

        int parameterIndex = (method.access & Opcodes.ACC_STATIC) == 0 ? 1 : 0;

        for (AbstractInsnNode instruction = action.getPrevious(); instruction != null; instruction = instruction.getPrevious()) {
            if (!(instruction instanceof FieldInsnNode) || instruction.getOpcode() != Opcodes.GETFIELD)
                continue;

            FieldInsnNode field = (FieldInsnNode) instruction;
            AbstractInsnNode load = previousReal(instruction);
            AbstractInsnNode constant = nextReal(instruction);

            if (!"I".equals(field.desc) || !(load instanceof VarInsnNode) || ((VarInsnNode) load).var != parameterIndex)
                continue;

            Integer value = integerConstant(constant);

            if (value != null)
                return new ControlCondition(field.owner, field.name, value);
        }

        return null;
    }

    private static boolean writesTrueBoolean(MethodNode method) {
        for (AbstractInsnNode instruction = method.instructions.getFirst(); instruction != null; instruction = instruction.getNext()) {
            if (instruction.getOpcode() == Opcodes.PUTFIELD && instruction instanceof FieldInsnNode
                && "Z".equals(((FieldInsnNode) instruction).desc)) {
                AbstractInsnNode value = previousReal(instruction);

                if (value != null && value.getOpcode() == Opcodes.ICONST_1)
                    return true;
            }
        }

        return false;
    }

    private static Integer integerConstant(AbstractInsnNode instruction) {
        if (instruction == null)
            return null;

        int opcode = instruction.getOpcode();

        if (opcode >= Opcodes.ICONST_M1 && opcode <= Opcodes.ICONST_5)
            return Integer.valueOf(opcode - Opcodes.ICONST_0);

        if (instruction instanceof IntInsnNode)
            return Integer.valueOf(((IntInsnNode) instruction).operand);

        if (instruction instanceof LdcInsnNode && ((LdcInsnNode) instruction).cst instanceof Integer)
            return (Integer) ((LdcInsnNode) instruction).cst;

        return null;
    }

    private static AbstractInsnNode previousReal(AbstractInsnNode start) {
        AbstractInsnNode instruction = start == null ? null : start.getPrevious();

        while (instruction != null && instruction.getOpcode() < 0)
            instruction = instruction.getPrevious();

        return instruction;
    }

    private static AbstractInsnNode nextReal(AbstractInsnNode start) {
        AbstractInsnNode instruction = start == null ? null : start.getNext();

        while (instruction != null && instruction.getOpcode() < 0)
            instruction = instruction.getNext();

        return instruction;
    }

    private static boolean isScreen(String name, Map<String, String> superTypes, String screenBaseName) {
        String current = name;

        while (current != null) {
            if (screenBaseName.equals(current))
                return true;

            current = superTypes.get(current);
        }

        return false;
    }

    private static boolean containsChat(List<ChatPlan> plans, ChatPlan candidate) {
        for (ChatPlan plan : plans) {
            if (plan.describe().equals(candidate.describe()))
                return true;
        }

        return false;
    }

    static final class IndexedCode {
        final AutomationPlan plan;
        final Map<String, String> superTypes;

        IndexedCode(AutomationPlan plan, Map<String, String> superTypes) {
            this.plan = plan;
            this.superTypes = superTypes;
        }
    }

    private static final class DirectCandidate {
        final ClassNode type;
        final DirectConnectPlan plan;

        DirectCandidate(ClassNode type, DirectConnectPlan plan) {
            this.type = type;
            this.plan = plan;
        }
    }

    static final class ScreenSetter {
        final String owner;
        final String name;
        final String descriptor;

        ScreenSetter(String owner, String name, String descriptor) {
            this.owner = owner;
            this.name = name;
            this.descriptor = descriptor;
        }
    }

    private static final class ControlCondition {
        final String owner;
        final String fieldName;
        final Integer id;

        ControlCondition(String owner, String fieldName, Integer id) {
            this.owner = owner;
            this.fieldName = fieldName;
            this.id = id;
        }
    }

    private static final class ConstructorInvocation {
        final String descriptor;
        final Object[] arguments;

        ConstructorInvocation(String descriptor, Object[] arguments) {
            this.descriptor = descriptor;
            this.arguments = arguments;
        }
    }

    enum TransitionTargetKind {
        GAME,
        SELF,
        CONSTRUCTED,
        PARENT
    }

}
