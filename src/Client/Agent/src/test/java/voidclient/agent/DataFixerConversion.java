package voidclient.agent;

import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.mojang.datafixers.DSL;
import com.mojang.datafixers.DataFix;
import com.mojang.datafixers.DataFixer;
import com.mojang.datafixers.DataFixerBuilder;
import com.mojang.datafixers.TypeRewriteRule;
import com.mojang.datafixers.schemas.Schema;
import com.mojang.datafixers.types.templates.TypeTemplate;
import com.mojang.serialization.Dynamic;
import com.mojang.serialization.JsonOps;
import java.lang.reflect.Method;
import java.util.Collections;
import java.util.Map;
import java.util.concurrent.Executor;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.function.Supplier;

public final class DataFixerConversion {
    private static final DSL.TypeReference DataType = () -> "test_data";

    public static String run() throws Exception {
        DataFixerBuilder builder = new DataFixerBuilder(1) {
            @Override
            public DataFixer buildUnoptimized() {
                throw new AssertionError("Warm-up deferral must preserve the original builder construction");
            }
        };
        builder.addSchema(0, TestSchema::new);
        Schema output = builder.addSchema(1, TestSchema::new);
        builder.addFixer(new DataFix(output, false) {
            @Override
            protected TypeRewriteRule makeRule() {
                return fixTypeEverywhereTyped("update value", getInputSchema().getType(DataType), typed ->
                    typed.update(DSL.fieldFinder("value", DSL.string()), value -> value + "-updated"));
            }
        });

        AtomicInteger warmups = new AtomicInteger();
        Executor executor = task -> {
            warmups.incrementAndGet();
            task.run();
        };
        Method build;
        try {
            build = DataFixerBuilder.class.getMethod("buildOptimized", Executor.class);
        } catch (NoSuchMethodException exception) {
            build = DataFixerBuilder.class.getMethod("build", Executor.class);
        }
        DataFixer fixer = (DataFixer) build.invoke(builder, executor);
        JsonObject input = new JsonObject();
        input.addProperty("value", "old");
        Dynamic<JsonElement> migrated = fixer.update(DataType, new Dynamic<JsonElement>(JsonOps.INSTANCE, input), 0, 1);
        return warmups.get() + ":" + migrated.get("value").asString().result().orElse("missing");
    }

    private static final class TestSchema extends Schema {
        TestSchema(int version, Schema parent) {
            super(version, parent);
        }

        @Override
        public Map<String, Supplier<TypeTemplate>> registerEntities(Schema schema) {
            return Collections.emptyMap();
        }

        @Override
        public Map<String, Supplier<TypeTemplate>> registerBlockEntities(Schema schema) {
            return Collections.emptyMap();
        }

        @Override
        public void registerTypes(Schema schema, Map<String, Supplier<TypeTemplate>> entities, Map<String, Supplier<TypeTemplate>> blockEntities) {
            schema.registerType(true, DataType, () -> DSL.fields("value", DSL.constType(DSL.string())));
        }
    }
}
